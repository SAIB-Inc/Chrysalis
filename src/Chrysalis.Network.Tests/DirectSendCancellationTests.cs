using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class DirectSendCancellationTests
{
    private static readonly byte[] VictimPayload = [0xde, 0xad, 0xbe, 0xef];
    private static readonly byte[] KeepAlivePayload = [1, 2];

    [Fact]
    public async Task CancellationDuringStalledFlushLeavesSendPendingUntilConnectionEnds()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        using CancellationTokenSource sender = new();
        _ = DirectSendShutdownTests.ParkFlush(bearer.Output);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        Task send = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(VictimPayload), sender.Token);
        Task queued = plexer.SubscribeClient(ProtocolType.KeepAlive).EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload));
        try
        {
            Assert.Equal(1, bearer.Output.Flushes);
            await sender.CancelAsync();
            _ = await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAny(send, queued).WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(send.IsCompleted, "Caller cancellation completed the send during a stalled flush.");
            Assert.False(queued.IsCompleted, "The next send escaped the write permit during a stalled flush.");
            Assert.Equal(1, bearer.Output.Flushes);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(send, queued).WaitAsync(TimeSpan.FromSeconds(5)));
        }

        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => send);
        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => queued);
        Assert.Equal(1, bearer.Output.Flushes);
    }

    [Fact]
    public async Task CancellationAfterPermitBeforeWriteCannotLeakThroughAnotherChannelsFlush()
    {
        using CancellationTokenSource victimCancellation = new();
        using CancelOnReadMemory payload = new(VictimPayload);
        ReadOnlySequence<byte> chunk = new(payload.Memory);
        await RunRaceAsync(chunk, victimCancellation, () => payload.Arm(victimCancellation.Cancel), false);
    }

    [Theory]
    [InlineData("muxed", 1)]
    [InlineData("raw", 1)]
    [InlineData("batch", 2)]
    public async Task CancellationInsideCommitStillDeliversWholeFrames(string kind, int copies)
    {
        Pipe bearer = new(new PipeOptions(pauseWriterThreshold: 0));
        using SemaphoreSlim gate = new(1, 1);
        using CancellationTokenSource cancel = new();
        AgentChannel victim = ChannelFor(ProtocolType.BlockFetch, new CancelOnAdvanceWriter(bearer.Writer, cancel), gate);
        AgentChannel keepAlive = ChannelFor(ProtocolType.KeepAlive, bearer.Writer, gate);
        byte[] wire = MuxSegmentCodec.Encode(new MuxSegment(new MuxSegmentHeader(0, ProtocolType.BlockFetch,
            (ushort)VictimPayload.Length, false), new ReadOnlySequence<byte>(VictimPayload))).ToArray();
        try
        {
            await (kind switch
            {
                "muxed" => victim.EnqueueChunkAsync(new ReadOnlySequence<byte>(VictimPayload), cancel.Token),
                "raw" => victim.WriteRawSegmentAsync(wire, cancel.Token),
                _ => victim.WriteBatchRawSegmentsAsync(wire, copies, cancel.Token)
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(cancel.IsCancellationRequested);
            await keepAlive.EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload));
            List<Frame> frames = await ReadThroughKeepAliveAsync(bearer.Reader);
            Assert.Equal(copies + 1, frames.Count);
            Assert.All(frames.Take(copies), frame =>
            {
                Assert.Equal(ProtocolType.BlockFetch, frame.Protocol);
                Assert.Equal(VictimPayload, frame.Payload);
            });
        }
        finally
        {
            await bearer.Reader.CompleteAsync();
            await bearer.Writer.CompleteAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawOrBatchCancellationWhileQueuedDoesNotCommit(bool batch)
    {
        using SemaphoreSlim gate = new(0, 1);
        using CancellationTokenSource cancel = new();
        SendWriter writer = new();
        AgentChannel channel = ChannelFor(ProtocolType.BlockFetch, writer, gate);
        Task send = batch ? channel.WriteBatchRawSegmentsAsync(VictimPayload, 2, cancel.Token) : channel.WriteRawSegmentAsync(VictimPayload, cancel.Token);
        try
        {
            Assert.False(send.IsCompleted);
            await cancel.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(writer.Bytes);
            Assert.Equal(0, writer.Flushes);
        }
        finally { _ = gate.Release(); }
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
        AgentChannel holder = ChannelFor(ProtocolType.NodeChainSync, bearer.Writer, bearerWriteLock);
        AgentChannel victim = ChannelFor(ProtocolType.BlockFetch, bearer.Writer, bearerWriteLock);
        AgentChannel keepAlive = ChannelFor(ProtocolType.KeepAlive, bearer.Writer, bearerWriteLock);
        Task first = holder.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[65_533]));
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
            long unflushed = bearer.Writer.UnflushedBytes;
            await keepAlive.EnqueueChunkAsync(new ReadOnlySequence<byte>(KeepAlivePayload)).WaitAsync(TimeSpan.FromSeconds(5));
            List<Frame> frames = await ReadThroughKeepAliveAsync(bearer.Reader);
            Assert.True(error is null or OperationCanceledException, $"Unexpected enqueue error: {error}");
            bool rejected = error is OperationCanceledException;
            if (arm is not null) { Assert.True(rejected, "Cancellation during CopyTo must be observed before Advance"); }
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
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, send, cancellation).WaitAsync(TimeSpan.FromSeconds(5)));
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

    private static AgentChannel ChannelFor(ProtocolType protocol, PipeWriter writer, SemaphoreSlim gate) => new(
        protocol, writer, Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, gate);

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
