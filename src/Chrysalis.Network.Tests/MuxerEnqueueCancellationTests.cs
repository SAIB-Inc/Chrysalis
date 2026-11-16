using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class MuxerEnqueueCancellationTests
{
    private static readonly byte[] VictimPayload = [0xde, 0xad, 0xbe, 0xef];
    private static readonly byte[] KeepAlivePayload = [1, 2];

    [Fact]
    public async Task CancellationAfterPermitBeforeWriteCannotLeakThroughAnotherChannelsFlush()
    {
        using CancellationTokenSource victimCancellation = new();
        using CancelOnReadMemory payload = new(VictimPayload);
        ReadOnlySequence<byte> chunk = new(payload.Memory);
        await RunRaceAsync(chunk, victimCancellation, () => payload.Arm(victimCancellation.Cancel), false);
    }

    [Fact]
    public async Task CancellationInsideWriteCommitsAndCompletesRatherThanRejectingTheRecord()
    {
        Pipe bearer = new(new PipeOptions(pauseWriterThreshold: 0));
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource victimCancellation = new();
        using CancellationTokenSource stop = new();
        CancelOnAdvanceWriter writer = new(muxer.Writer, victimCancellation);
        AgentChannel victim = ChannelFor(ProtocolType.BlockFetch, writer, bearer.Writer, bearerWriteLock);
        AgentChannel keepAlive = ChannelFor(ProtocolType.KeepAlive, writer, bearer.Writer, bearerWriteLock);
        Task run = muxer.RunAsync(stop.Token);
        try
        {
            Exception? error = await Record.ExceptionAsync(() => victim.EnqueueChunkAsync(new ReadOnlySequence<byte>(VictimPayload), victimCancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));
            long unflushed = muxer.Writer.UnflushedBytes;
            await keepAlive.EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload)).WaitAsync(TimeSpan.FromSeconds(5));
            List<Frame> frames = await ReadThroughKeepAliveAsync(bearer.Reader);
            Assert.True(victimCancellation.IsCancellationRequested);
            Assert.True(error is null, $"A record whose Write already advanced must commit, not be rejected: {error}");
            Assert.Equal(0, unflushed);
            Frame sent = Assert.Single(frames, frame => frame.Protocol == ProtocolType.BlockFetch);
            Assert.Equal(VictimPayload, sent.Payload);
            Assert.Equal(KeepAlivePayload, Assert.Single(frames, frame => frame.Protocol == ProtocolType.KeepAlive).Payload);
        }
        finally
        {
            await stop.CancelAsync();
            Exception? error = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(error is null or OperationCanceledException, $"Unexpected consumer error: {error}");
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    [Fact]
    public async Task CancellationAfterWriteDoesNotAbortBackpressureButMuxerStopDoes()
    {
        Pipe bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource caller = new();
        AgentChannel channel = ChannelFor(ProtocolType.BlockFetch, muxer.Writer, bearer.Writer, bearerWriteLock);
        Task send = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[65_533]), caller.Token);
        Task queued = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload));
        try
        {
            Assert.False(send.IsCompleted);
            await caller.CancelAsync();
            _ = await Assert.ThrowsAsync<TimeoutException>(() => send.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(send.IsCompleted);
            Assert.False(queued.IsCompleted);
            Assert.Equal(0, muxer.Writer.UnflushedBytes);
            muxer.Dispose();
            ObjectDisposedException stopped = await Assert.ThrowsAsync<ObjectDisposedException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            InvalidOperationException waiting = await Assert.ThrowsAsync<InvalidOperationException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(stopped, waiting.InnerException);
            if (bearer.Reader.TryRead(out ReadResult result))
            {
                Assert.True(result.Buffer.IsEmpty, "No consumer ever sent a frame to the bearer");
                bearer.Reader.AdvanceTo(result.Buffer.End);
            }
        }
        finally
        {
            muxer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(send, queued).WaitAsync(TimeSpan.FromSeconds(5)));
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    public static TheoryData<int> RaceIterations => [.. Enumerable.Range(0, 50)];

    [Theory]
    [MemberData(nameof(RaceIterations))]
    public async Task PermitGrantCancellationRaceNeverLeaksARejectedFrame(int iteration)
    {
        byte[] bytes = new byte[16_384];
        bytes.AsSpan().Fill((byte)iteration);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using CancellationTokenSource victimCancellation = new();
            await RunRaceAsync(new ReadOnlySequence<byte>(bytes), victimCancellation, null, true);
        }
    }

    private static async Task RunRaceAsync(ReadOnlySequence<byte> payload, CancellationTokenSource victimCancellation,
        Action? arm, bool cancelAfterHolder)
    {
        Pipe bearer = new(new PipeOptions(pauseWriterThreshold: 0));
        using SemaphoreSlim bearerWriteLock = new(0, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        using CancellationTokenSource stop = new();
        AgentChannel holder = ChannelFor(ProtocolType.NodeChainSync, muxer.Writer, bearer.Writer, bearerWriteLock);
        AgentChannel victim = ChannelFor(ProtocolType.BlockFetch, muxer.Writer, bearer.Writer, bearerWriteLock);
        AgentChannel keepAlive = ChannelFor(ProtocolType.KeepAlive, muxer.Writer, bearer.Writer, bearerWriteLock);
        Task first = holder.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[65_533]));
        Task run = muxer.RunAsync(stop.Token);
        arm?.Invoke();
        Task send = victim.EnqueueChunkAsync(payload, victimCancellation.Token);
        Task cancellation = cancelAfterHolder ? first.ContinueWith(_ => victimCancellation.Cancel(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default) : Task.CompletedTask;
        _ = bearerWriteLock.Release();
        try
        {
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Exception? error = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
            long unflushed = muxer.Writer.UnflushedBytes;
            await keepAlive.EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload)).WaitAsync(TimeSpan.FromSeconds(5));
            List<Frame> frames = await ReadThroughKeepAliveAsync(bearer.Reader);
            Assert.True(error is null or OperationCanceledException, $"Unexpected enqueue error: {error}");
            bool rejected = error is OperationCanceledException;
            if (arm is not null) { Assert.True(rejected, "Cancellation during CopyTo must be observed before Write"); }
            Assert.True(!rejected || !frames.Any(frame => frame.Protocol == ProtocolType.BlockFetch),
                $"A canceled send leaked to bearer: unflushed={unflushed}, exception={error?.GetType().Name}");
            Assert.Equal(0, unflushed);
            Assert.Equal(new byte[65_533], Assert.Single(frames, frame => frame.Protocol == ProtocolType.NodeChainSync).Payload);
            Assert.Equal(KeepAlivePayload, Assert.Single(frames, frame => frame.Protocol == ProtocolType.KeepAlive).Payload);
            if (!rejected)
            {
                Assert.Equal(payload.ToArray(), Assert.Single(frames, frame => frame.Protocol == ProtocolType.BlockFetch).Payload);
            }
            Assert.Equal(rejected ? 2 : 3, frames.Count);
        }
        finally
        {
            await victimCancellation.CancelAsync();
            await stop.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, send, cancellation).WaitAsync(TimeSpan.FromSeconds(5)));
            Exception? error = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(error is null or OperationCanceledException, $"Unexpected consumer error: {error}");
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    private static async Task<List<Frame>> ReadThroughKeepAliveAsync(PipeReader reader)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        List<Frame> frames = [];
        while (true)
        {
            ReadResult result = await reader.ReadAsync(deadline.Token);
            ReadOnlySequence<byte> remaining = result.Buffer;
            while (remaining.Length >= ProtocolConstants.SegmentHeaderSize)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(remaining.Slice(0, ProtocolConstants.SegmentHeaderSize));
                long length = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                if (remaining.Length < length) { break; }
                Assert.False(header.Mode);
                frames.Add(new Frame(header.ProtocolId, remaining.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength).ToArray()));
                remaining = remaining.Slice(length);
                if (header.ProtocolId == ProtocolType.KeepAlive)
                {
                    Assert.True(remaining.IsEmpty);
                    reader.AdvanceTo(remaining.End);
                    return frames;
                }
            }
            reader.AdvanceTo(remaining.Start, remaining.End);
            Assert.False(result.IsCompleted, "Bearer ended before the KeepAlive sentinel");
        }
    }

    private static AgentChannel ChannelFor(ProtocolType protocol, PipeWriter writer, PipeWriter bearer, SemaphoreSlim gate) => new(
        protocol, writer, bearer, Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, gate);

    private sealed record Frame(ProtocolType Protocol, byte[] Payload);

    private sealed class CancelOnReadMemory(byte[] bytes) : MemoryManager<byte>
    {
        private Action? _cancel;
        internal void Arm(Action cancel) => _cancel = cancel;
        public override Span<byte> GetSpan()
        {
            Interlocked.Exchange(ref _cancel, null)?.Invoke();
            return bytes;
        }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class CancelOnAdvanceWriter(PipeWriter writer, CancellationTokenSource cancellation) : PipeWriter
    {
        public override long UnflushedBytes => writer.UnflushedBytes;
        public override void Advance(int bytes)
        {
            writer.Advance(bytes);
            cancellation.Cancel();
        }
        public override Memory<byte> GetMemory(int sizeHint = 0) => writer.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => writer.GetSpan(sizeHint);
        public override void CancelPendingFlush() => writer.CancelPendingFlush();
        public override void Complete(Exception? exception = null) => writer.Complete(exception);
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => writer.FlushAsync(cancellationToken);
    }
}
