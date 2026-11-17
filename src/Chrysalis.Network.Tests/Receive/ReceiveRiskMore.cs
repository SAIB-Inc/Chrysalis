using System.Threading.Channels;
using System.Diagnostics;
using System.Reflection;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

public sealed class SharedReaderRiskTests
{
    [Theory]
    [InlineData("ready", false, 1)]
    [InlineData("registration", false, 1)]
    [InlineData("later", false, 1)]
    [InlineData("later", true, 1)]
    [InlineData("later", false, 2)]
    public async Task S1S2S4S5S18BlockedOrSpinningHandlersNeverHoldTheReader(string timing, bool spin, int blockers)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> fetch = demuxer.Subscribe(ProtocolType.BlockFetch);
        ChannelReader<ReadOnlyMemory<byte>> local = demuxer.Subscribe(ProtocolType.LocalStateQuery);
        ChannelReader<ReadOnlyMemory<byte>> keep = demuxer.Subscribe(ProtocolType.KeepAlive);
        ChannelReader<ReadOnlyMemory<byte>> chain = demuxer.Subscribe(ProtocolType.NodeChainSync);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task firstHandler = HandlerAsync(fetch, entered, true);
        Task secondHandler = blockers == 2 ? HandlerAsync(local, second, false) : Task.CompletedTask;
        Task<ReadOnlyMemory<byte>> alive = keep.ReadAsync().AsTask();
        Task<ReadOnlyMemory<byte>> sync = chain.ReadAsync().AsTask();
        ReceiveSchedule initial = new("initial");
        initial.Segment(ProtocolType.BlockFetch, ReceiveVectors.Body());
        if (blockers == 2) { initial.Segment(ProtocolType.LocalStateQuery, ReceiveVectors.Body()); }
        ReceiveSchedule next = new("next");
        next.Segment(ProtocolType.KeepAlive, ReceiveVectors.Body());
        next.Segment(ProtocolType.NodeChainSync, ReceiveVectors.Body());
        bearer.Deliver(initial.Wire.ToArray());
        if (timing == "ready") { bearer.Deliver(next.Wire.ToArray()); }
        if (timing == "registration") { bearer.CompleteNextDuringRegistration([.. next.Wire]); }
        Task run = demuxer.RunAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (blockers == 2) { await second.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            if (timing == "later") { Assert.True(bearer.Pending); bearer.Deliver(next.Wire.ToArray()); }
            _ = await Task.WhenAll(alive, sync).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(firstHandler.IsCompleted);
            if (blockers == 2) { Assert.False(secondHandler.IsCompleted); }
        }
        finally
        {
            _ = release.TrySetResult();
            await Task.WhenAll(firstHandler, secondHandler).WaitAsync(TimeSpan.FromSeconds(10));
            demuxer.Dispose();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        async Task HandlerAsync(ChannelReader<ReadOnlyMemory<byte>> reader, TaskCompletionSource signal, bool inline)
        {
            _ = await reader.ReadAsync().ConfigureAwait(false);
            Assert.False(demuxer.ReaderRoleHeld);
            Assert.False(demuxer.Gate.IsHeldByCurrentThread);
            if (inline) { Assert.Contains(new StackTrace().GetFrames(), frame => frame.GetMethod()?.DeclaringType == typeof(Demuxer) && frame.GetMethod()?.Name == "Turn"); }
            signal.SetResult();
            Block(release.Task, spin);
        }
    }

    private static void Block(Task release, bool spin)
    {
        if (!spin) { Assert.True(release.Wait(TimeSpan.FromSeconds(15))); return; }
        long end = Environment.TickCount64 + 15000;
        while (!release.IsCompleted && Environment.TickCount64 < end) { Thread.SpinWait(100); }
        Assert.True(release.IsCompleted);
    }

    [Fact]
    public async Task S3StartParksPreexistingReadersAndRunsOffTheCallerStack()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        Task<ReadOnlyMemory<byte>> read = demuxer.Subscribe(ProtocolType.BlockFetch).ReadAsync().AsTask();
        Assert.False(read.IsCompleted);
        Assert.Equal(0, bearer.Reads);
        Task run = demuxer.RunAsync(CancellationToken.None);
        _ = Assert.Throws<InvalidOperationException>(() => { _ = demuxer.RunAsync(CancellationToken.None); });
        bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, ReceiveVectors.Body()).Wire.ToArray(), end: true);
        _ = await read.WaitAsync(TimeSpan.FromSeconds(10));
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S9QueuedMessagesDrainBeforeCleanOrPartialMessageEnd(bool partial)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        ReceiveSchedule schedule = new("end");
        schedule.Segment(ProtocolType.BlockFetch, ReceiveVectors.Body());
        if (partial) { schedule.Segment(ProtocolType.BlockFetch, ReceiveVectors.Body(23)[..^1]); }
        bearer.Deliver(schedule.Wire.ToArray(), end: true);
        Task run = demuxer.RunAsync(CancellationToken.None);
        if (partial) { _ = await Assert.ThrowsAsync<EndOfStreamException>(() => run.WaitAsync(TimeSpan.FromSeconds(10))); }
        else { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(ReceiveVectors.Body(), (await queue.ReadAsync()).ToArray());
        if (partial) { Assert.True((await queue.ReadAsync()).IsEmpty); }
        ConnectionClosedException error = await Assert.ThrowsAsync<ConnectionClosedException>(() => queue.ReadAsync().AsTask());
        if (partial)
        {
            _ = Assert.IsType<EndOfStreamException>(error.InnerException);
            _ = await Assert.ThrowsAsync<EndOfStreamException>(() => queue.WaitToReadAsync().AsTask());
        }
        else { Assert.Null(error.InnerException); Assert.False(await queue.WaitToReadAsync()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S15OneDequeueRestartsReadAndObservesALaterEnd(bool fault)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ProtocolQueue queue = (ProtocolQueue)demuxer.Subscribe(ProtocolType.BlockFetch);
        ReceiveSchedule schedule = new("stop");
        for (int i = 0; i < 65; i++) { schedule.Segment(ProtocolType.BlockFetch, ReceiveVectors.Body()); }
        bearer.Deliver(schedule.Wire.ToArray());
        Task run = demuxer.RunAsync(CancellationToken.None);
        await UntilAsync(() => demuxer.Stopped);
        Assert.Equal(64, queue.Count);
        Assert.True(queue.TryRead(out _));
        Assert.Equal(64, queue.Count);
        Assert.True(bearer.Pending);
        if (fault) { bearer.Fail(new IOException("scripted read fault")); }
        else { bearer.Deliver([], end: true); }
        if (fault) { _ = await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(10))); }
        else { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S16BudgetBoundsCallbackAndCallerAndDrainsOneLargeResult(bool caller)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        ReceiveSchedule schedule = new("budget");
        for (int i = 0; i < (caller ? 65 : 1); i++) { schedule.Segment(ProtocolType.BlockFetch, ReceiveVectors.Body()); }
        for (int i = 0; i < 12; i++) { schedule.Segment((ProtocolType)99, new byte[65535]); schedule.Segment((ProtocolType)99, []); }
        long checkpoint = -1;
        Task handler = caller ? Task.CompletedTask : CaptureAsync();
        bearer.Deliver(schedule.Wire.ToArray());
        Task run = demuxer.RunAsync(CancellationToken.None);
        if (caller)
        {
            await UntilAsync(() => demuxer.Stopped);
            int thread = Environment.CurrentManagedThreadId;
            long before = bearer.ThreadBytes(thread);
            Assert.True(queue.TryRead(out _));
            checkpoint = bearer.ThreadBytes(thread) - before;
        }
        else { await handler.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.InRange(checkpoint, 256 * 1024, (256 * 1024) + 65543);
        await UntilAsync(() => bearer.Pending);
        Assert.Equal(schedule.Wire.Count, bearer.Consumed);
        demuxer.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        async Task CaptureAsync()
        {
            _ = await queue.ReadAsync().ConfigureAwait(false);
            Assert.False(demuxer.ReaderRoleHeld);
            checkpoint = bearer.ThreadBytes(Environment.CurrentManagedThreadId);
        }
    }

    [Fact]
    public async Task QueueMembersCancellationAndSingleWaiterPreserveSlots()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        using CancellationTokenSource cancel = new();
        Task<bool> wait = queue.WaitToReadAsync(cancel.Token).AsTask();
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => { _ = await queue.ReadAsync(); });
        await cancel.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Task<bool> next = queue.WaitToReadAsync().AsTask();
        ReceiveSchedule schedule = new("empty");
        schedule.Segment(ProtocolType.BlockFetch, []);
        bearer.Deliver(schedule.Wire.ToArray(), end: true);
        Task run = demuxer.RunAsync(CancellationToken.None);
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryPeek(out ReadOnlyMemory<byte> peek));
        Assert.True(peek.IsEmpty);
        Assert.True(queue.TryRead(out ReadOnlyMemory<byte> empty));
        Assert.True(empty.IsEmpty);
        Assert.Equal(0, queue.Count);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await queue.WaitToReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S16DisposeOrCancellationStopsAContinuouslyReadyReadAtItsFirstBudget(bool cancel)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        using CancellationTokenSource token = new();
        ReceiveSchedule schedule = new("ready end");
        for (int i = 0; i < 20; i++) { schedule.Segment((ProtocolType)99, new byte[65535]); }
        bearer.Deliver(schedule.Wire.ToArray());
        bearer.Advanced = _ => { if (cancel) { token.Cancel(); } else { demuxer.Dispose(); } };
        Task run = demuxer.RunAsync(token.Token);
        if (cancel) { _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10))); }
        else { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.InRange(bearer.Consumed, 256 * 1024, (256 * 1024) + 65543);
        Assert.True(bearer.Consumed < schedule.Wire.Count);
        Assert.False(bearer.HoldingResult);
        Assert.Equal(0, bearer.CompletionCalls);
    }

    [Fact]
    public async Task S9ReadFaultDrainsQueuedMessagesAndS11PendingDisposeDoesNotCompleteTheReader()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, ReceiveVectors.Body()).Wire.ToArray());
        Task run = demuxer.RunAsync(CancellationToken.None);
        await UntilAsync(() => bearer.Pending);
        IOException cause = new("scripted fault");
        bearer.Fail(cause);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Equal(ReceiveVectors.Body(), (await queue.ReadAsync()).ToArray());
        ConnectionClosedException error = await Assert.ThrowsAsync<ConnectionClosedException>(() => queue.ReadAsync().AsTask());
        Assert.Same(cause, error.InnerException);
        demuxer.Dispose();
        Assert.Equal(0, bearer.CompletionCalls);
        error = await Assert.ThrowsAsync<ConnectionClosedException>(() => demuxer.Subscribe(ProtocolType.KeepAlive).ReadAsync().AsTask());
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public async Task S6EachDequeueRoutesTheStoppedSegmentOnTheCallersThreadBeforeReturning()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> fetch = demuxer.Subscribe(ProtocolType.BlockFetch);
        ChannelReader<ReadOnlyMemory<byte>> keep = demuxer.Subscribe(ProtocolType.KeepAlive);
        Task alive = ResumeAsync();
        ReceiveSchedule schedule = new("restart");
        for (int i = 0; i < 66; i++) { schedule.Segment(ProtocolType.BlockFetch, [0]); }
        schedule.Segment(ProtocolType.KeepAlive, [0]);
        bearer.Deliver(schedule.Wire.ToArray());
        Task run = demuxer.RunAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => demuxer.Stopped);
            Assert.Equal(64, fetch.Count);
            Assert.Equal(64 * 9, bearer.Consumed);
            int thread = Environment.CurrentManagedThreadId;
            long before = bearer.ThreadBytes(thread);
            Assert.True(fetch.TryRead(out _));
            Assert.Equal(9, bearer.ThreadBytes(thread) - before);
            Assert.Equal(65 * 9, bearer.Consumed);
            Assert.Equal(64, fetch.Count);
            Assert.True(demuxer.Stopped);
            Assert.True(fetch.TryRead(out _));
            Assert.Equal(64, fetch.Count);
            Assert.Equal(schedule.Wire.Count, bearer.Consumed);
            await alive.WaitAsync(TimeSpan.FromSeconds(10));
            await UntilAsync(() => bearer.Pending);
        }
        finally { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }

        async Task ResumeAsync()
        {
            _ = await keep.ReadAsync().ConfigureAwait(false);
            Assert.DoesNotContain(new StackTrace().GetFrames(), frame =>
                frame.GetMethod()?.DeclaringType == typeof(Demuxer) && frame.GetMethod()?.Name == "CallerTurn");
            Assert.False(demuxer.ReaderRoleHeld);
            Assert.False(demuxer.Gate.IsHeldByCurrentThread);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S7OneReadResumesOneParkedReaderThenDeliversAllMessagesWithoutAnotherWait(bool separateSegments)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        Task consumer = ConsumeAsync();
        ReceiveSchedule schedule = new("one read");
        if (separateSegments) { for (int i = 0; i < 32; i++) { schedule.Segment(ProtocolType.BlockFetch, [0]); } }
        else { schedule.Segment(ProtocolType.BlockFetch, new byte[32]); }
        Task run = demuxer.RunAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => bearer.Pending);
            bearer.Deliver(schedule.Wire.ToArray());
            await consumer.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, bearer.Reads); // Initial pending read, then the next registered read.
            Assert.True(bearer.Pending);
            Assert.Equal(0, queue.Count);
        }
        finally { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }

        async Task ConsumeAsync()
        {
            ReadOnlyMemory<byte> item = await queue.ReadAsync().ConfigureAwait(false);
            int thread = Environment.CurrentManagedThreadId;
            Assert.Contains(new StackTrace().GetFrames(), frame =>
                frame.GetMethod()?.DeclaringType == typeof(Demuxer) && frame.GetMethod()?.Name == "Turn");
            Assert.False(demuxer.ReaderRoleHeld);
            int messages = item.Length;
            while (messages < 32)
            {
                ValueTask<ReadOnlyMemory<byte>> next = queue.ReadAsync();
                Assert.True(next.IsCompletedSuccessfully);
                messages += (await next.ConfigureAwait(false)).Length;
                Assert.Equal(thread, Environment.CurrentManagedThreadId);
            }
            Assert.Equal(32, messages);
        }
    }

    [Theory]
    [InlineData("before")]
    [InlineData("parked")]
    [InlineData("race")]
    [InlineData("after")]
    public async Task S8CallerCancellationNeverCancelsTheBearerOrLosesAnItem(string timing)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        Task run = demuxer.RunAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => bearer.Pending);
            for (int iteration = 0; iteration < (timing == "race" ? 64 : 1); iteration++)
            {
                using CancellationTokenSource token = new();
                if (timing == "before") { await token.CancelAsync(); }
                Task<ReadOnlyMemory<byte>> read = queue.ReadAsync(token.Token).AsTask();
                if (timing is "before" or "parked")
                {
                    await token.CancelAsync();
                    _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
                    bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, [0]).Wire.ToArray());
                }
                else if (timing == "race")
                {
                    using Barrier start = new(2);
                    await Task.WhenAll(Task.Run(() => { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); token.Cancel(); }),
                        Task.Run(() => { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, [0]).Wire.ToArray()); }));
                }
                else { bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, [0]).Wire.ToArray()); }
                Exception? outcome = await Record.ExceptionAsync(async () => { Assert.Equal(new byte[] { 0 }, (await read).ToArray()); });
                if (outcome is OperationCanceledException) { Assert.Equal(new byte[] { 0 }, (await queue.ReadAsync()).ToArray()); }
                else { Assert.Null(outcome); }
                await token.CancelAsync();
                Assert.False(queue.TryRead(out _));
                Assert.Equal(0, bearer.CancelPendingReadCalls);
                Assert.Equal(0, bearer.CallerTokenCancellations);
            }
        }
        finally { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task S13RejectsEveryPairOfOutstandingReads(bool firstWait, bool secondWait)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        Task first = firstWait ? queue.WaitToReadAsync().AsTask() : queue.ReadAsync().AsTask();
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (secondWait) { _ = await queue.WaitToReadAsync(); } else { _ = await queue.ReadAsync(); }
        });
        demuxer.Dispose();
        if (firstWait) { Assert.False(await (Task<bool>)first); }
        else { _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => first); }
    }

    [Fact]
    public async Task S14TenThousandReadsFromResumedContinuationsKeepTheStackBounded()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        byte[] frame = [.. OwnedReceiveTests.Frames(ProtocolType.BlockFetch, [0]).Wire];
        int minFrames = int.MaxValue;
        int maxFrames = 0;
        Task consumer = ConsumeAsync();
        Task run = demuxer.RunAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => bearer.Pending);
            bearer.Deliver(frame);
            await consumer.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.InRange(maxFrames - minFrames, 0, 32);
            Assert.InRange(maxFrames, 1, 128);
        }
        finally { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }

        async Task ConsumeAsync()
        {
            for (int i = 0; i < 10_000; i++)
            {
                Assert.Equal(new byte[] { 0 }, (await queue.ReadAsync().ConfigureAwait(false)).ToArray());
                Assert.False(demuxer.ReaderRoleHeld);
                Assert.False(demuxer.Gate.IsHeldByCurrentThread);
                int frames = new StackTrace().FrameCount;
                minFrames = Math.Min(minFrames, frames);
                maxFrames = Math.Max(maxFrames, frames);
                if (i < 9_999) { bearer.Deliver(frame); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadGenerationRolloverDoesNotBecomeARetrySentinel(bool notificationFirst)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ProtocolQueue queue = (ProtocolQueue)demuxer.Subscribe(ProtocolType.BlockFetch);
        typeof(ProtocolQueue).GetField("_generation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(queue, notificationFirst ? int.MaxValue - 1 : int.MaxValue);
        if (notificationFirst)
        {
            ValueTask<bool> wait = queue.WaitToReadAsync();
            ProtocolQueue.Delivery delivery;
            lock (demuxer.Gate) { delivery = queue.EnqueueLocked(new byte[] { 0 }); }
            delivery.Complete(false);
            Assert.True(await wait);
            Assert.True(queue.TryRead(out _));
        }
        ValueTask<ReadOnlyMemory<byte>> read = queue.ReadAsync();
        ProtocolQueue.Delivery next;
        lock (demuxer.Gate) { next = queue.EnqueueLocked(new byte[] { 1 }); }
        next.Complete(false);
        Assert.Equal(new byte[] { 1 }, (await read).ToArray());
    }

    [Fact]
    public async Task S8StaleCancellationCannotCancelTheNextGeneration()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ProtocolQueue queue = (ProtocolQueue)demuxer.Subscribe(ProtocolType.BlockFetch);
        using CancellationTokenSource token = new();
        using ManualResetEventSlim entered = new();
        ValueTask<ReadOnlyMemory<byte>> first = queue.ReadAsync(token.Token);
        using CancellationTokenRegistration observer = token.Token.Register(entered.Set);
        Thread cancellation = new(token.Cancel) { IsBackground = true };
        ValueTask<ReadOnlyMemory<byte>> second;
        lock (demuxer.Gate)
        {
            cancellation.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(SpinWait.SpinUntil(() => (cancellation.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
            ProtocolQueue.Delivery delivery = queue.EnqueueLocked(new byte[] { 0 });
            delivery.Complete(false);
            Assert.Equal(new byte[] { 0 }, ConsumeCompleted(first).ToArray());
            second = queue.ReadAsync();
        }
        Assert.True(cancellation.Join(TimeSpan.FromSeconds(10)));
        Assert.False(second.IsCompleted);
        ProtocolQueue.Delivery next;
        lock (demuxer.Gate) { next = queue.EnqueueLocked(new byte[] { 1 }); }
        next.Complete(false);
        Assert.Equal(new byte[] { 1 }, (await second).ToArray());
    }

    [Fact]
    public async Task S2RegistrationCallbackCannotNestTurns()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> queue = demuxer.Subscribe(ProtocolType.BlockFetch);
        Task handler = ResumeAsync();
        bearer.CompleteNextDuringRegistration([.. OwnedReceiveTests.Frames(ProtocolType.BlockFetch, [0]).Wire]);
        Task run = demuxer.RunAsync(CancellationToken.None);
        try { await handler.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, bearer.RegistrationCompletions); }
        finally { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }

        async Task ResumeAsync()
        {
            _ = await queue.ReadAsync().ConfigureAwait(false);
            _ = Assert.Single(new StackTrace().GetFrames(), frame => frame.GetMethod()?.DeclaringType == typeof(Demuxer) && frame.GetMethod()?.Name == "Turn");
            Assert.False(demuxer.ReaderRoleHeld);
            Assert.False(demuxer.Gate.IsHeldByCurrentThread);
        }
    }

    // The cancellation race must consume this already-completed value while holding Gate.
    private static ReadOnlyMemory<byte> ConsumeCompleted(ValueTask<ReadOnlyMemory<byte>> read)
    {
        Assert.True(read.IsCompletedSuccessfully);
        return read.GetAwaiter().GetResult();
    }

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(1, deadline.Token); }
    }
}
