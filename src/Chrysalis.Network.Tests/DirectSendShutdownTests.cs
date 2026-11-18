using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public sealed class DirectSendShutdownTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly ReadOnlySequence<byte> Payload = new([1]);

    [Theory]
    [InlineData("cancel run")]
    [InlineData("bearer fault")]
    [InlineData("dispose")]
    [InlineData("dispose before run")]
    public async Task EndSettlesStalledAndQueuedSendWithoutTheCallersToken(string kind)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        using CancellationTokenSource senders = new();
        using CancellationTokenSource stop = new();
        TaskCompletionSource<FlushResult> stalled = ParkFlush(bearer.Output);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
        Task? run = kind == "dispose before run" ? null : plexer.RunAsync(stop.Token);
        Task first = channel.EnqueueChunkAsync(Payload, senders.Token);
        Task second = channel.EnqueueChunkAsync(Payload, senders.Token);
        IOException injected = new("scripted bearer failure");
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(CancellationToken.None, bearer.Output.FlushToken);
            switch (kind)
            {
                case "cancel run": await stop.CancelAsync(); break;
                case "bearer fault": stalled.SetException(injected); break;
                default: plexer.Dispose(); break;
            }
            ConnectionClosedException holder = await Closed(first);
            Assert.Same(holder.InnerException, (await Closed(second)).InnerException);
            Assert.Same(holder.InnerException, (await Closed(channel.EnqueueChunkAsync(Payload))).InnerException);
            if (kind == "bearer fault")
            {
                Assert.Same(injected, holder.InnerException);
                Assert.Same(injected, await Assert.ThrowsAsync<IOException>(() => run!.WaitAsync(Deadline)));
            }
            else if (kind == "cancel run")
            {
                OperationCanceledException cause = Assert.IsAssignableFrom<OperationCanceledException>(holder.InnerException);
                Assert.Equal(stop.Token, cause.CancellationToken);
                _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run!.WaitAsync(Deadline));
            }
            else { _ = Assert.IsType<ObjectDisposedException>(holder.InnerException); }
            Assert.False(senders.IsCancellationRequested);
            Assert.Equal(1, bearer.Output.Flushes);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, second, run ?? Task.CompletedTask).WaitAsync(Deadline));
        }
    }

    public static TheoryData<int> ShutdownIterations => [.. Enumerable.Range(0, 25)];

    [Theory]
    [MemberData(nameof(ShutdownIterations))]
    public async Task ConcurrentDisposeRunCancellationAndQueuedSendsKeepOneCause(int iteration)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        using CancellationTokenSource stop = new();
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([(byte)iteration]));
        Assert.Equal((byte)iteration, bearer.Output.Bytes[^1]);
        _ = ParkFlush(bearer.Output);
        Task run = plexer.RunAsync(stop.Token);
        Task holder = channel.EnqueueChunkAsync(Payload);
        Task waiter = channel.EnqueueChunkAsync(Payload);
        Assert.False(holder.IsCompleted);
        Assert.False(waiter.IsCompleted);
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Task[]> producer = Task.Run(async () =>
        {
            await start.Task;
            return Enumerable.Range(0, 64).Select(_ => channel.EnqueueChunkAsync(Payload)).ToArray();
        });
        Task[] shutdowns = [DisposeAsync(), DisposeAsync(), Task.Run(async () => { await start.Task; await stop.CancelAsync(); })];
        start.SetResult();
        try
        {
            await Task.WhenAll(shutdowns).WaitAsync(Deadline);
            Task[] extra = await producer.WaitAsync(Deadline);
            Exception? cause = (await Closed(holder)).InnerException;
            Assert.True(cause is ObjectDisposedException or OperationCanceledException);
            foreach (Task pending in extra.Append(waiter)) { Assert.Same(cause, (await Closed(pending)).InnerException); }
            Exception? stopped = await Record.ExceptionAsync(() => run.WaitAsync(Deadline));
            Assert.True(stopped is InvalidOperationException or OperationCanceledException);
            Assert.Equal(2, bearer.Output.Flushes);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(holder, waiter, run).WaitAsync(Deadline));
        }

        Task DisposeAsync() => Task.Run(async () => { await start.Task; plexer.Dispose(); plexer.Dispose(); });
    }

    private static async Task<ConnectionClosedException> Closed(Task send) =>
        await Assert.ThrowsAsync<ConnectionClosedException>(() => send.WaitAsync(Deadline));

    internal static TaskCompletionSource<FlushResult> ParkFlush(SendWriter writer)
    {
        TaskCompletionSource<FlushResult> stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        writer.OnCancelPendingFlush = () => stalled.TrySetResult(new FlushResult(isCanceled: true, isCompleted: false));
        writer.AsyncFlush = () => new ValueTask<FlushResult>(stalled.Task);
        return stalled;
    }

    [Fact]
    public async Task DisposeRacesLiveProductionWithoutReclaimingBorrowedPayloads()
    {
        using PipeBearer bearer = new();
        using Plexer plexer = new(bearer);
        using CancellationTokenSource senders = new();
        Task run = plexer.RunAsync(CancellationToken.None);
        Task[] producers = [.. Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            ProtocolType protocol = (ProtocolType)(index + 2);
            AgentChannel channel = plexer.SubscribeClient(protocol);
            byte[] payload = new byte[125];
            payload.AsSpan().Fill((byte)protocol);
            try
            {
                for (int message = 0; message < 10_000; message++)
                {
                    await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload), senders.Token);
                }
            }
            catch (ConnectionClosedException ex) when (ex.InnerException is ObjectDisposedException) { }
            catch (OperationCanceledException) when (senders.IsCancellationRequested) { }
        }))];
        try
        {
            await bearer.Output.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(Task.Run(plexer.Dispose), Task.Run(plexer.Dispose)).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(5));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Deadline));
            Assert.False(senders.IsCancellationRequested);
            plexer.Dispose();
        }
        finally
        {
            plexer.Dispose();
            await senders.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(producers.Append(run)).WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }

    private sealed class ValidatingWriter : PipeWriter
    {
        private readonly byte[] _buffer = new byte[256];
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _frames;
        public Task Ready => _ready.Task;
        public override void Advance(int bytes)
        {
            Assert.Equal(133, bytes);
            ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(4));
            Assert.InRange(protocol, (ushort)2, (ushort)5);
            Assert.Equal(125, BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(6)));
            Assert.All(_buffer.AsSpan(8, 125).ToArray(), value => Assert.Equal((byte)protocol, value));
            if (++_frames == 64) { _ready.SetResult(); }
        }
        public override Memory<byte> GetMemory(int sizeHint = 0) => _buffer;
        public override Span<byte> GetSpan(int sizeHint = 0) => _buffer;
        public override void CancelPendingFlush() { }
        public override void Complete(Exception? exception = null) { }
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FlushResult(false, false));
        }
    }

    private sealed class PipeBearer : IBearer
    {
        private readonly Pipe _input = new();
        public PipeReader Reader => _input.Reader;
        public PipeWriter Writer => Output;
        internal ValidatingWriter Output { get; } = new();

        public void Dispose()
        {
            _input.Writer.Complete();
            _input.Reader.Complete();
        }
    }
}
