using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class MuxerShutdownTests
{
    private const string StoppedMessage = "The muxer has stopped accepting protocol messages.";
    [Theory]
    [InlineData("cancellation")]
    [InlineData("bearer failure")]
    [InlineData("dispose")]
    [InlineData("dispose before run")]
    public async Task StoppingMuxerSettlesHolderAndQueuedSenderWithoutTheirCancellation(string shutdown)
    {
        Pipe bearer = new();
        IOException failure = new("Test bearer failed");
        using SemaphoreSlim bearerWriteLock = new(shutdown is "cancellation" or "dispose" ? 0 : 1, 1);
        using Muxer muxer = new(shutdown == "bearer failure" ? new FaultWriter(failure) : bearer.Writer,
            ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource senders = new();
        using CancellationTokenSource stopMuxer = new();
        AgentChannel channel = ChannelFor(muxer.Writer, bearerWriteLock);
        Task first = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[65_533]), senders.Token);
        Task second = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[1]), senders.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Task? run = null;
        try
        {
            switch (shutdown)
            {
                case "cancellation":
                    run = muxer.RunAsync(stopMuxer.Token);
                    await stopMuxer.CancelAsync();
                    _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
                    break;
                case "bearer failure":
                    run = muxer.RunAsync(stopMuxer.Token);
                    Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5))));
                    break;
                case "dispose":
                    run = muxer.RunAsync(stopMuxer.Token);
                    muxer.Dispose();
                    _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
                    break;
                case "dispose before run":
                    muxer.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shutdown), shutdown, "Unknown shutdown route");
            }

            InvalidOperationException queued = await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(StoppedMessage, queued.Message);
            InvalidOperationException later = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[1])).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(StoppedMessage, later.Message);
            switch (shutdown)
            {
                case "bearer failure":
                    Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(5))));
                    Assert.Same(failure, queued.InnerException);
                    Assert.Same(failure, later.InnerException);
                    break;
                case "cancellation":
                    OperationCanceledException canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.NotEqual(senders.Token, canceled.CancellationToken);
                    _ = Assert.IsAssignableFrom<OperationCanceledException>(queued.InnerException);
                    _ = Assert.IsAssignableFrom<OperationCanceledException>(later.InnerException);
                    break;
                case "dispose":
                case "dispose before run":
                    ObjectDisposedException disposed = await Assert.ThrowsAsync<ObjectDisposedException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Same(disposed, Assert.IsType<ObjectDisposedException>(queued.InnerException));
                    Assert.Same(disposed, Assert.IsType<ObjectDisposedException>(later.InnerException));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shutdown), shutdown, "Unknown shutdown route");
            }
            Assert.False(senders.IsCancellationRequested);
            AssertNoBearerBytes(bearer.Reader);
        }
        finally
        {
            await senders.CancelAsync();
            await stopMuxer.CancelAsync();
            muxer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)));
            if (run is not null) { _ = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5))); }
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    public static TheoryData<int> ShutdownIterations => [.. Enumerable.Range(0, 25)];

    [Theory]
    [MemberData(nameof(ShutdownIterations))]
    public async Task ConcurrentDisposeCancellationAndQueuedSendsAreIdempotent(int iteration)
    {
        Pipe bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource senders = new();
        using CancellationTokenSource stopMuxer = new();
        AgentChannel channel = ChannelFor(muxer.Writer, bearerWriteLock);
        byte[] prelude = new byte[125];
        prelude.AsSpan().Fill((byte)(iteration + 1));
        BinaryPrimitives.WriteInt32BigEndian(prelude, iteration);
        Task holder = Task.CompletedTask;
        Task waiter = Task.CompletedTask;
        Task run = Task.CompletedTask;
        Task producer = Task.CompletedTask;
        Task[] shutdowns = [];
        ConcurrentQueue<Task> extra = new();
        try
        {
            await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(prelude), senders.Token);
            run = muxer.RunAsync(stopMuxer.Token);
            await AssertPreludeAsync(bearer.Reader, prelude);
            await bearerWriteLock.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            holder = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[65_533]), senders.Token);
            waiter = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[1]), senders.Token);
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            producer = Task.Run(async () =>
            {
                await start.Task;
                for (int index = 0; index < 64; index++)
                {
                    extra.Enqueue(channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[1]), senders.Token));
                }
            });
            shutdowns = [.. Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                muxer.Dispose();
                muxer.Dispose();
            })), Task.Run(async () =>
            {
                await start.Task;
                await stopMuxer.CancelAsync();
            })];
            start.SetResult();
            await Task.WhenAll(shutdowns.Append(producer)).WaitAsync(TimeSpan.FromSeconds(5));
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Exception? firstError = await Record.ExceptionAsync(() => holder.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(firstError is OperationCanceledException or ObjectDisposedException,
                $"Race {iteration} holder must report shutdown, not corruption: {firstError}");
            InvalidOperationException waiting = await Assert.ThrowsAsync<InvalidOperationException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(StoppedMessage, waiting.Message);
            Assert.True(waiting.InnerException is OperationCanceledException or ObjectDisposedException,
                $"Race {iteration} waiter must preserve shutdown cause: {waiting}");
            if (firstError is ObjectDisposedException)
            {
                Assert.Same(firstError, waiting.InnerException);
            }
            else
            {
                OperationCanceledException holderCanceled = Assert.IsAssignableFrom<OperationCanceledException>(firstError);
                OperationCanceledException waiterCause = Assert.IsAssignableFrom<OperationCanceledException>(waiting.InnerException);
                Assert.Equal(holderCanceled.CancellationToken, waiterCause.CancellationToken);
            }
            foreach (Task pending in extra)
            {
                InvalidOperationException rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(StoppedMessage, rejected.Message);
                Assert.Same(waiting.InnerException, rejected.InnerException);
            }
            Assert.Equal(64, extra.Count);
            Assert.False(senders.IsCancellationRequested);
            AssertNoBearerBytes(bearer.Reader);
            muxer.Dispose();
        }
        finally
        {
            await senders.CancelAsync();
            await stopMuxer.CancelAsync();
            muxer.Dispose();
            // The collection is thread-safe even if a failing timeout leaves
            // the producer running; join it before observing its tasks too.
            _ = await Record.ExceptionAsync(() => Task.WhenAll(shutdowns.Append(producer)).WaitAsync(TimeSpan.FromSeconds(5)));
            _ = await Record.ExceptionAsync(() => Task.WhenAll(extra.Append(holder).Append(waiter).Append(run)).WaitAsync(TimeSpan.FromSeconds(5)));
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    private static async Task AssertPreludeAsync(PipeReader reader, byte[] payload)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        ReadResult result = await reader.ReadAsync(deadline.Token);
        try
        {
            Assert.Equal(payload.Length + ProtocolConstants.SegmentHeaderSize, result.Buffer.Length);
            MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(result.Buffer.Slice(0, ProtocolConstants.SegmentHeaderSize));
            Assert.Equal(ProtocolType.BlockFetch, header.ProtocolId);
            Assert.Equal(payload.Length, header.PayloadLength);
            Assert.False(header.Mode);
            Assert.Equal(payload, result.Buffer.Slice(ProtocolConstants.SegmentHeaderSize).ToArray());
        }
        finally
        {
            reader.AdvanceTo(result.Buffer.End);
        }
    }

    private static void AssertNoBearerBytes(PipeReader reader)
    {
        if (reader.TryRead(out ReadResult result))
        {
            try { Assert.True(result.Buffer.IsEmpty, "Shutdown emitted an unexpected or corrupt frame"); }
            finally { reader.AdvanceTo(result.Buffer.End); }
        }
    }

    [Fact]
    public async Task DisposeRacesLiveProductionWithoutReclaimingBorrowedPayloads()
    {
        ValidatingWriter bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer, ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource senders = new();
        Task run = muxer.RunAsync(CancellationToken.None);
        Task[] producers = [.. Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            ProtocolType protocol = (ProtocolType)(index + 2);
            AgentChannel channel = new(protocol, muxer.Writer, bearer,
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, bearerWriteLock);
            byte[] payload = new byte[125];
            payload.AsSpan().Fill((byte)protocol);
            try
            {
                for (int message = 0; message < 10_000; message++)
                {
                    await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload), senders.Token);
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException ex) when (ex.Message == StoppedMessage && ex.InnerException is ObjectDisposedException) { }
            catch (OperationCanceledException) when (senders.IsCancellationRequested) { }
        }))];
        try
        {
            await bearer.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(Task.Run(muxer.Dispose), Task.Run(muxer.Dispose)).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(5));
            Exception? stopped = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            _ = Assert.IsAssignableFrom<OperationCanceledException>(stopped);
            Assert.False(senders.IsCancellationRequested);
            muxer.Dispose();
        }
        finally
        {
            muxer.Dispose();
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

    private static AgentChannel ChannelFor(PipeWriter writer, SemaphoreSlim bearerWriteLock) => new(
        ProtocolType.BlockFetch, writer, writer,
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, bearerWriteLock);

    private sealed class FaultWriter(IOException failure) : PipeWriter
    {
        public override void Advance(int bytes) => throw failure;
        public override Memory<byte> GetMemory(int sizeHint = 0) => throw failure;
        public override Span<byte> GetSpan(int sizeHint = 0) => throw failure;
        public override void CancelPendingFlush() { }
        public override void Complete(Exception? exception = null) { }
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => ValueTask.FromException<FlushResult>(failure);
    }
}
