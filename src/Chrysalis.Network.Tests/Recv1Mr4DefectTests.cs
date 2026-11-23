using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Threading.Channels;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Test;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.KeepAlive;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Tests.Receive;
using Xunit;
using ProtocolType = Chrysalis.Network.Core.ProtocolType;

namespace Chrysalis.Network.Tests;

public sealed class Recv1Mr4DefectTests
{
    private static readonly byte[] BatchDoneCbor = [0x81, 0x05];
    private static readonly byte[] StartBatchCbor = [0x81, 0x02];
    internal static readonly FieldInfo KeepAliveTaskField =
        typeof(PeerClient).GetField("_keepAliveTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ChannelBufferWaiterField =
        typeof(ChannelBuffer).GetField("_waiter", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData("queued")]
    [InlineData("pending")]
    public async Task DecodeErrorOnPlexerChannelClearsCursorAndEndsConnection(string mode)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel keepAlive = plexer.SubscribeClient(ProtocolType.KeepAlive);
        AgentChannel chainSync = plexer.SubscribeClient(ProtocolType.NodeChainSync);
        ProtocolQueue keepAliveQueue = GetQueue(keepAlive);
        ChannelBuffer buffer = new(keepAlive);
        ChainSync sync = new(chainSync, ProtocolType.NodeChainSync);
        ReceiveSchedule wire = new("d1");
        wire.Segment(ProtocolType.KeepAlive, BatchDoneCbor);
        wire.Segment(ProtocolType.KeepAlive, StartBatchCbor);
        byte[] both = [.. wire.Wire];
        Task run = plexer.RunAsync(CancellationToken.None);
        try
        {
            CborDecodeException decodeError;
            if (mode == "queued")
            {
                _ = await bearer.Input.WriteAsync(both, CancellationToken.None);
                Assert.True(SpinWait.SpinUntil(() => keepAliveQueue.Count == 2, N5Wire.Deadline));
                decodeError = await Assert.ThrowsAsync<CborDecodeException>(async () => _ = await buffer.ReceiveFullMessageAsync<KeepAliveMessage>(CancellationToken.None));
            }
            else
            {
                async Task<KeepAliveMessage> ReceiveKeepAliveAsync() => await buffer.ReceiveFullMessageAsync<KeepAliveMessage>(CancellationToken.None);
                Task<KeepAliveMessage> call1 = ReceiveKeepAliveAsync();
                Assert.False(call1.IsCompleted);
                _ = await bearer.Input.WriteAsync(both, CancellationToken.None);
                decodeError = await Assert.ThrowsAsync<CborDecodeException>(() => call1.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            }

            ConnectionClosedException call2 = await Assert.ThrowsAsync<ConnectionClosedException>(async () => _ = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None));
            Assert.Same(decodeError, call2.InnerException);
            ConnectionClosedException sibling = await Assert.ThrowsAsync<ConnectionClosedException>(() => sync.DoneAsync(CancellationToken.None));
            Assert.Same(decodeError, sibling.InnerException);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
    }

    [Fact]
    public async Task DecodeErrorOnCallerChannelClearsCursorAndStaysFailed()
    {
        using ProtocolReplay replay = new(ProtocolType.KeepAlive, BatchDoneCbor);
        ChannelBuffer buffer = new(replay.Channel);
        CborDecodeException decodeError = await Assert.ThrowsAsync<CborDecodeException>(async () => _ = await buffer.ReceiveFullMessageAsync<KeepAliveMessage>(CancellationToken.None));
        CborDecodeException second = await Assert.ThrowsAsync<CborDecodeException>(async () => _ = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None));
        Assert.Same(decodeError, second);
    }

    [Fact]
    public async Task ReplyLimitExpirySwallowsSecondaryCleanupError()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
        bearer.Output.OnCancelPendingFlush = static () => throw new IOException("secondary cleanup error");
        Task pending = client.LocalTxMonitor.GetSizesAsync(CancellationToken.None);
        try
        {
            ManualTimer timer = clock.Timers[^1];
            Exception? escaped = Record.Exception(timer.Fire);
            Assert.Null(escaped);
            ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => pending.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            ReplyTimeoutException timeout = Assert.IsType<ReplyTimeoutException>(closed.InnerException);
            Assert.Same(timeout, client.GetPlexerException());
            await client.DisposeAsync();
            Assert.True(bearer.IsDisposed);
        }
        finally
        {
            bearer.Output.OnCancelPendingFlush = null;
            _ = await Record.ExceptionAsync(() => pending.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData("node")]
    [InlineData("peer")]
    public async Task StartAfterDisposeThrowsObjectDisposedOnBothClients(string kind)
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        if (kind == "node")
        {
            await using NodeClient client = NodeClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            await AssertStartAfterDisposeAsync(bearer, clock, client, () => client.StartAsync(2, CancellationToken.None), client.IsPlexerHealthy, client.GetPlexerException);
        }
        else
        {
            await using PeerClient client = PeerClient.CreateFromBearer(bearer);
            client.Replies.Clock = clock;
            await AssertStartAfterDisposeAsync(bearer, clock, client, () => client.StartAsync(42, TimeSpan.FromHours(1), CancellationToken.None), client.IsPlexerHealthy, client.GetPlexerException);
        }
    }

    [Fact]
    public async Task KeepAliveCookieMismatchEndsTheConnection()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using PeerClient client = await N5LifecycleTests.StartedPeerAsync(bearer, clock, CancellationToken.None);
        Task keepAliveTask = (Task)KeepAliveTaskField.GetValue(client)!;
        byte[] badReply = await TriggerKeepAliveAndBuildWrongCookieReplyAsync(bearer, clock.Timers[^1]);
        await N5Wire.ReplyAsync(bearer, ProtocolType.KeepAlive, badReply);
        _ = await Record.ExceptionAsync(() => keepAliveTask.WaitAsync(N5Wire.Deadline, CancellationToken.None));

        InvalidOperationException err = Assert.IsType<InvalidOperationException>(client.GetKeepAliveException());
        Assert.False(client.IsPlexerHealthy());
        Assert.Same(err, client.GetPlexerException());
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.ChainSync.DoneAsync(CancellationToken.None));
        Assert.Same(err, closed.InnerException);
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("io-fault")]
    public async Task PlexerRunAsyncMatchesTheEndContract(string kind)
    {
        if (kind == "dispose")
        {
            using SendBearer bearer = new();
            using Plexer plexer = new(bearer);
            AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
            Task run = plexer.RunAsync(CancellationToken.None);
            plexer.Dispose();
            Exception? stopped = await Record.ExceptionAsync(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            Assert.Null(stopped);
            Assert.Equal(TaskStatus.RanToCompletion, plexer.Completion.Status);
            Assert.False(plexer.IsPlexerHealthy());
            Assert.Null(plexer.GetPlexerException());
            ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([1]), CancellationToken.None));
            _ = Assert.IsType<ObjectDisposedException>(later.InnerException);
        }
        else
        {
            using ScriptedReceiveBearer bearer = new();
            using Plexer plexer = new(bearer);
            IOException injected = new("scripted read failure");
            Task run = plexer.RunAsync(CancellationToken.None);
            try
            {
                Assert.True(SpinWait.SpinUntil(() => bearer.Pending, N5Wire.Deadline));
                bearer.Fail(injected);
                ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
                Assert.Same(injected, closed.InnerException);
                Assert.True(plexer.Completion.IsFaulted);
                Assert.Same(injected, plexer.GetPlexerException());
                Assert.False(plexer.IsPlexerHealthy());
                plexer.Dispose();
                Assert.True(plexer.Completion.IsFaulted);
                Assert.Same(injected, plexer.GetPlexerException());
            }
            finally
            {
                plexer.Dispose();
                _ = await Record.ExceptionAsync(() => run.WaitAsync(N5Wire.Deadline, CancellationToken.None));
            }
        }
    }

    [Fact]
    public async Task NodeClientCleanPeerCloseReportsConnectionClosedException()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
        await bearer.Input.CompleteAsync();
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.ChainSync.NextRequestAsync(CancellationToken.None).WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.Null(closed.InnerException);
        await client.DisposeAsync();
        ConnectionClosedException ex = Assert.IsType<ConnectionClosedException>(client.GetPlexerException());
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task PeerClientCleanPeerCloseEndsKeepAliveQuietly()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using PeerClient client = await N5LifecycleTests.StartedPeerAsync(bearer, clock, CancellationToken.None);
        Task keepAliveTask = (Task)KeepAliveTaskField.GetValue(client)!;
        await bearer.Input.CompleteAsync();
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.ChainSync.NextRequestAsync(CancellationToken.None).WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.Null(closed.InnerException);
        clock.Timers[^1].Fire();
        Exception? keepAliveStopped = await Record.ExceptionAsync(() => keepAliveTask.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        await client.DisposeAsync();
        Assert.False(client.IsKeepAliveHealthy());
        Assert.Null(client.GetKeepAliveException());
        Assert.Null(keepAliveStopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtocolQueueBlockedWaitsAllocateZeroBytes(bool waitToRead)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ProtocolQueue queue = (ProtocolQueue)demuxer.Subscribe(ProtocolType.BlockFetch);
        using CancellationTokenSource cts = new();
        ReadOnlyMemory<byte> payload = BatchDoneCbor;

        void RunBatch()
        {
            for (int i = 0; i < 100; i++)
            {
                if (waitToRead)
                {
                    ValueTask<bool> wait = queue.WaitToReadAsync(cts.Token);
                    Assert.True(CompleteAndConsume(wait, demuxer, queue, payload));
                    Assert.True(queue.TryRead(out _));
                }
                else
                {
                    ValueTask<ReadOnlyMemory<byte>> read = queue.ReadAsync(cts.Token);
                    ReadOnlyMemory<byte> received = CompleteAndConsume(read, demuxer, queue, payload);
                    Assert.True(received.Length == payload.Length);
                }
            }
        }

        long allocated = AllocationAssert.Measure(RunBatch, RunBatch);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ChannelBufferReceiveWaitAllocatesZeroBytesOverDecode()
    {
        using BufferFixture fixture = new();
        using CancellationTokenSource cts = new();
        ReadOnlyMemory<byte> payload = BatchDoneCbor;
        BlockFetchMessage? last = null;

        void MeasureSync()
        {
            for (int i = 0; i < 100; i++)
            {
                lock (fixture.Demuxer.Gate) { _ = fixture.Queue.EnqueueLocked(payload); }
                last = ConsumeCompleted(fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(cts.Token));
            }
        }

        long decodeBytes = MeasureDecodeBytes(payload);
        long syncBytes = AllocationAssert.Measure(MeasureSync, MeasureSync, () => _ = Assert.IsType<BatchDone>(last));
        long blockedBytes = MeasureBlockedReceiveBytes(fixture, payload, cts.Token);

        Assert.Equal(decodeBytes, syncBytes);
        Assert.Equal(decodeBytes, blockedBytes);

        Task<BlockFetchMessage> preCanceled = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(new CancellationToken(canceled: true)).AsTask();
        Assert.True(preCanceled.IsCanceled);
        ValueTask<BlockFetchMessage> firstPending = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        Task<BlockFetchMessage> concurrent = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask();
        Assert.True(concurrent.IsFaulted);
        _ = Assert.IsType<InvalidOperationException>(concurrent.Exception!.InnerExceptions[0]);
        _ = CompleteAndConsume(firstPending, fixture.Demuxer, fixture.Queue, payload);
    }

    [Theory]
    [InlineData("cancel-before-value")]
    [InlineData("cancel-after-value")]
    [InlineData("complete-then-cancel")]
    [InlineData("cancel-then-complete")]
    public async Task ReceiveWaiterHandlesCancellationAndCompletionRaces(string scenario)
    {
        using BufferFixture fixture = new();
        ReadOnlyMemory<byte> payload = BatchDoneCbor;
        _ = Assert.IsType<BatchDone>(CompleteAndConsume(fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None), fixture.Demuxer, fixture.Queue, payload));
        object waiter = ChannelBufferWaiterField.GetValue(fixture.Buffer)!;

        using CancellationTokenSource cts = new();
        ValueTask<BlockFetchMessage> pending = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(cts.Token);
        Assert.False(pending.IsCompleted);
        if (scenario == "cancel-before-value")
        {
            await cts.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
        else if (scenario == "cancel-after-value")
        {
            ProtocolQueue.Delivery delivery;
            lock (fixture.Demuxer.Gate) { delivery = fixture.Queue.EnqueueLocked(payload); }
            delivery.Complete(inline: true);
            Assert.True(pending.IsCompletedSuccessfully);
            await cts.CancelAsync();
            _ = Assert.IsType<BatchDone>(ConsumeCompleted(pending));
        }
        else if (scenario == "complete-then-cancel")
        {
            ProtocolQueue.Delivery delivery;
            lock (fixture.Demuxer.Gate) { delivery = fixture.Queue.EnqueueLocked(payload); }
            await cts.CancelAsync();
            delivery.Complete(inline: true);
            _ = Assert.IsType<BatchDone>(ConsumeCompleted(pending));
        }
        else
        {
            await cts.CancelAsync();
            ProtocolQueue.Delivery delivery;
            lock (fixture.Demuxer.Gate) { delivery = fixture.Queue.EnqueueLocked(payload); }
            delivery.Complete(inline: true);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None));
            _ = Assert.IsType<BatchDone>(ConsumeCompleted(fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None)));
        }

        AssertWaiterReusedZeroAlloc(fixture, payload, waiter);
    }

    [Theory]
    [InlineData("second-await-completed")]
    [InlineData("second-await-pending")]
    [InlineData("stale-generation-while-reused")]
    public async Task ReceiveWaiterRejectsSecondAwaitAndStaleGeneration(string scenario)
    {
        using BufferFixture fixture = new();
        ReadOnlyMemory<byte> payload = BatchDoneCbor;

        ValueTask<BlockFetchMessage> first = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        object waiter = ChannelBufferWaiterField.GetValue(fixture.Buffer)!;
        if (scenario == "second-await-completed")
        {
            _ = Assert.IsType<BatchDone>(CompleteAndConsume(first, fixture.Demuxer, fixture.Queue, payload));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => _ = await first);
        }
        else if (scenario == "second-await-pending")
        {
            _ = Assert.Throws<InvalidOperationException>(() => _ = GetResultNow(first));
            Task<BlockFetchMessage> firstAwait = first.AsTask();
            Assert.False(firstAwait.IsCompleted);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(first.AsTask);
            ProtocolQueue.Delivery delivery;
            lock (fixture.Demuxer.Gate) { delivery = fixture.Queue.EnqueueLocked(payload); }
            delivery.Complete(inline: true);
            _ = Assert.IsType<BatchDone>(await firstAwait.WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
        else
        {
            _ = Assert.IsType<BatchDone>(CompleteAndConsume(first, fixture.Demuxer, fixture.Queue, payload));
            ValueTask<BlockFetchMessage> second = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
            Assert.False(second.IsCompleted);
            Assert.Same(waiter, ChannelBufferWaiterField.GetValue(fixture.Buffer));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => _ = await first);
            _ = Assert.IsType<BatchDone>(CompleteAndConsume(second, fixture.Demuxer, fixture.Queue, payload));
        }

        AssertWaiterReusedZeroAlloc(fixture, payload, waiter);
    }

    [Fact]
    public async Task OverlappingReceiveOnCallerChannelFaultsWithoutDisturbingFirst()
    {
        Channel<ReadOnlyMemory<byte>> input = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        Pipe unused = new();
        using SemaphoreSlim gate = new(1, 1);
        ChannelBuffer buffer = new(new AgentChannel(ProtocolType.BlockFetch, unused.Writer, input.Reader, gate));
        bool secondFaulted = false;
        try
        {
            ValueTask<BlockFetchMessage> first = buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
            Assert.False(first.IsCompleted);
            ValueTask<BlockFetchMessage> second = buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
            secondFaulted = second.IsFaulted;
            Assert.True(secondFaulted);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => _ = await second);
            Assert.True(input.Writer.TryWrite(BatchDoneCbor));
            _ = Assert.IsType<BatchDone>(await first.AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None));
            Assert.True(input.Writer.TryWrite(StartBatchCbor));
            _ = Assert.IsType<StartBatch>(await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask().WaitAsync(N5Wire.Deadline, CancellationToken.None));
        }
        finally
        {
            if (secondFaulted) { _ = input.Writer.TryComplete(); }
            await unused.Writer.CompleteAsync();
            await unused.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task UnconsumedCompletedReceiveRejectsSecondCallAndPreservesFirstValue()
    {
        using BufferFixture fixture = new();
        ReadOnlyMemory<byte> payload = BatchDoneCbor;

        ValueTask<BlockFetchMessage> first = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        object waiter = ChannelBufferWaiterField.GetValue(fixture.Buffer)!;
        ProtocolQueue.Delivery delivery;
        lock (fixture.Demuxer.Gate) { delivery = fixture.Queue.EnqueueLocked(payload); }
        delivery.Complete(inline: true);
        Assert.True(first.IsCompletedSuccessfully);

        ValueTask<BlockFetchMessage> second = fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        Assert.True(second.IsFaulted);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => _ = await second);

        _ = Assert.IsType<BatchDone>(ConsumeCompleted(first));
        AssertWaiterReusedZeroAlloc(fixture, payload, waiter);
    }

    [Fact]
    public async Task LocalStateQueryReleaseFailureAfterSuccessfulQueryPropagates()
    {
        using SendBearer bearer = new();
        await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, new ManualClock(), CancellationToken.None);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, [0x81, 0x01]);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, [0x82, 0x04, 0x06]);
        IOException releaseFault = new("release flush fault");
        int flushes = 0;
        bearer.Output.Flush = () => ++flushes == 3 ? throw releaseFault : default;
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => client.LocalStateQuery.GetCurrentEraAsync(CancellationToken.None).WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.Same(releaseFault, closed.InnerException);
    }

    [Fact]
    public async Task LocalStateQueryCancelDuringUnwindKeepsOriginalExceptionAndIsAcquired()
    {
        using SendBearer bearer = new();
        ManualClock clock = new();
        await using NodeClient client = await N5ReplyLimitTests.StartedNodeAsync(bearer, clock, CancellationToken.None);
        using CancellationTokenSource cancel = new();
        int flushes = 0;
        bearer.Output.Flush = () => { if (++flushes == 2) { clock.Timers[^1].OnDispose = cancel.Cancel; } return default; };
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, [0x81, 0x01]);
        await N5Wire.ReplyAsync(bearer, ProtocolType.LocalStateQuery, [0x82, 0x04, 0x00]);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.LocalStateQuery.GetCurrentProtocolParamsAsync(cancel.Token).WaitAsync(N5Wire.Deadline, CancellationToken.None));
        Assert.Contains("era 0", error.Message, StringComparison.Ordinal);
        Assert.True(cancel.IsCancellationRequested && client.LocalStateQuery.IsAcquired && flushes == 2);
    }

    internal static async Task<byte[]> TriggerKeepAliveAndBuildWrongCookieReplyAsync(SendBearer bearer, ManualTimer tick)
    {
        int sentBefore = bearer.Output.Bytes.Length;
        TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bearer.Output.Flush = () =>
        {
            _ = sent.TrySetResult();
            return default;
        };
        tick.Fire();
        await sent.Task.WaitAsync(N5Wire.Deadline, CancellationToken.None);
        MessageKeepAlive request = Assert.IsType<MessageKeepAlive>(
            CborSerializer.Deserialize<KeepAliveMessage>(bearer.Output.Bytes.AsMemory(sentBefore + ProtocolConstants.SegmentHeaderSize)));
        uint wrongCookie = (request.Cookie + 1u) & 0xFFFFu;
        return CborSerializer.Serialize(KeepAliveMessages.KeepAliveResponse(wrongCookie));
    }

    internal static ProtocolQueue GetQueue(AgentChannel channel) =>
        (ProtocolQueue)typeof(AgentChannel).GetField("_demuxReader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(channel)!;

    private static async Task AssertStartAfterDisposeAsync(SendBearer bearer, ManualClock clock, IAsyncDisposable client,
        Func<Task> start, Func<bool> isHealthy, Func<Exception?> getException)
    {
        await client.DisposeAsync();
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(start);
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(start);
        Assert.Empty(bearer.Output.Bytes);
        Assert.Empty(clock.Timers);
        Assert.False(isHealthy());
        Assert.Null(getException());
    }

    private static long MeasureDecodeBytes(ReadOnlyMemory<byte> payload)
    {
        BlockFetchMessage? last = null;
        void Run()
        {
            for (int i = 0; i < 100; i++)
            {
                last = CborSerializer.Deserialize<BlockFetchMessage>(payload, out int consumed);
                Assert.True(consumed == payload.Length);
            }
        }
        return AllocationAssert.Measure(Run, Run, () => _ = Assert.IsType<BatchDone>(last));
    }

    private static long MeasureBlockedReceiveBytes(BufferFixture fixture, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        BlockFetchMessage? last = null;
        void Run()
        {
            for (int i = 0; i < 100; i++)
            {
                last = CompleteAndConsume(fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(token), fixture.Demuxer, fixture.Queue, payload);
            }
        }
        return AllocationAssert.Measure(Run, Run, () => _ = Assert.IsType<BatchDone>(last));
    }

    private static void AssertWaiterReusedZeroAlloc(BufferFixture fixture, ReadOnlyMemory<byte> payload, object expectedWaiter)
    {
        _ = Assert.IsType<BatchDone>(CompleteAndConsume(fixture.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None), fixture.Demuxer, fixture.Queue, payload));
        Assert.Same(expectedWaiter, ChannelBufferWaiterField.GetValue(fixture.Buffer));
        using CancellationTokenSource nextCts = new();
        long decodeBytes = MeasureDecodeBytes(payload);
        long blockedBytes = MeasureBlockedReceiveBytes(fixture, payload, nextCts.Token);
        Assert.Equal(decodeBytes, blockedBytes);
        Assert.Same(expectedWaiter, ChannelBufferWaiterField.GetValue(fixture.Buffer));
    }

    private static T GetResultNow<T>(ValueTask<T> task) => task.GetAwaiter().GetResult();

    private static T ConsumeCompleted<T>(ValueTask<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully);
        return task.GetAwaiter().GetResult();
    }

    private static T CompleteAndConsume<T>(ValueTask<T> pending, Demuxer demuxer, ProtocolQueue queue, ReadOnlyMemory<byte> payload)
    {
        Assert.False(pending.IsCompleted);
        ProtocolQueue.Delivery delivery;
        lock (demuxer.Gate) { delivery = queue.EnqueueLocked(payload); }
        delivery.Complete(inline: true);
        return ConsumeCompleted(pending);
    }

    private sealed class BufferFixture : IDisposable
    {
        private readonly ScriptedReceiveBearer _bearer = new();
        private readonly Plexer _plexer;
        internal ChannelBuffer Buffer { get; }
        internal Demuxer Demuxer { get; }
        internal ProtocolQueue Queue { get; }
        internal BufferFixture()
        {
            _plexer = new(_bearer);
            AgentChannel channel = _plexer.SubscribeClient(ProtocolType.BlockFetch);
            Buffer = new(channel);
            Demuxer = (Demuxer)typeof(Plexer).GetField("_demuxer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_plexer)!;
            Queue = GetQueue(channel);
        }
        public void Dispose() { _plexer.Dispose(); _bearer.Dispose(); }
    }
}
