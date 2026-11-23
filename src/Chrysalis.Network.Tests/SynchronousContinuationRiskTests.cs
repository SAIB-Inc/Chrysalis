using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;
using Xunit.Abstractions;

namespace Chrysalis.Network.Tests;

public class SynchronousContinuationRiskTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("send and subscribe")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task DemuxContinuationCanReenterTransport(string action)
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        Connection connection = new(bearer);
        using Demuxer demuxer = new(bearer, connection);
        AgentChannel channel = new(ProtocolType.BlockFetch, bearer.Writer, demuxer.Subscribe(ProtocolType.BlockFetch), connection, ProtocolMode.Initiator);
        Task outside = Task.CompletedTask;
        Task handler = Task.CompletedTask;
        Task run = Task.CompletedTask;
        try
        {
            handler = HandleAsync();
            _ = await bearer.Incoming.WriteAsync(Frame(ProtocolType.BlockFetch, [1]), deadline.Token);
            if (action != "send and subscribe") { await bearer.Incoming.CompleteAsync(); }
            run = Task.Run(() => demuxer.RunAsync(deadline.Token), CancellationToken.None);
            await handler.WaitAsync(Deadline);
            await outside.WaitAsync(Deadline);
            if (action == "cancel")
            {
                Exception? canceled = await Record.ExceptionAsync(() => run.WaitAsync(Deadline));
                Assert.True(canceled is null or OperationCanceledException, $"Unexpected demux stop: {canceled}");
            }
            else
            {
                await run.WaitAsync(Deadline);
            }
            _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.ReadSegmentAsync().AsTask().WaitAsync(Deadline));
        }
        finally
        {
            await deadline.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(run, handler, outside).WaitAsync(Deadline));
        }

        async Task HandleAsync()
        {
            Assert.Equal(new byte[] { 1 }, (await channel.ReadSegmentAsync(deadline.Token).ConfigureAwait(false)).ToArray());
            output.WriteLine("Demux handler on RunAsync stack: {0}", HasRunFrame<Demuxer>());
            switch (action)
            {
                case "send and subscribe":
                    ChannelReader<ReadOnlyMemory<byte>> other = demuxer.Subscribe(ProtocolType.KeepAlive);
                    await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([2]), deadline.Token);
                    _ = await bearer.Incoming.WriteAsync(Frame(ProtocolType.KeepAlive, [3]), deadline.Token);
                    await bearer.Incoming.CompleteAsync();
                    Assert.Equal(new byte[] { 3 }, (await other.ReadAsync(deadline.Token)).ToArray());
                    break;
                case "cancel":
                    outside = Task.Run(deadline.Cancel, CancellationToken.None);
                    await deadline.CancelAsync();
                    break;
                case "dispose":
                    outside = Task.Run(demuxer.Dispose, CancellationToken.None);
                    demuxer.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowHandlerDoesNotStarveKeepAliveOrOtherProtocols(bool blocksSynchronously)
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> slow = demuxer.Subscribe(ProtocolType.BlockFetch);
        ChannelReader<ReadOnlyMemory<byte>> keepAlive = demuxer.Subscribe(ProtocolType.KeepAlive);
        ChannelReader<ReadOnlyMemory<byte>> chainSync = demuxer.Subscribe(ProtocolType.NodeChainSync);
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource asyncRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task handler = Task.CompletedTask;
        Task<ReadOnlyMemory<byte>> keepAliveRead = Task.FromResult(ReadOnlyMemory<byte>.Empty);
        Task<ReadOnlyMemory<byte>> chainRead = keepAliveRead;
        Task run = Task.CompletedTask;
        byte[] frames = [.. Frame(ProtocolType.BlockFetch, [1]), .. Frame(ProtocolType.KeepAlive, [2]),
            .. Frame(ProtocolType.NodeChainSync, [3])];
        try
        {
            handler = HandleAsync();
            keepAliveRead = keepAlive.ReadAsync(deadline.Token).AsTask();
            chainRead = chainSync.ReadAsync(deadline.Token).AsTask();
            _ = await bearer.Incoming.WriteAsync(frames, deadline.Token);
            await bearer.Incoming.CompleteAsync();
            run = Task.Run(() => demuxer.RunAsync(deadline.Token), CancellationToken.None);
            await entered.Task.WaitAsync(Deadline);
            // All three frames are already in one read. Two seconds allows ample
            // scheduling slack, but delivery must not depend on the slow handler
            // returning: a blocked protocol must not stop keep-alive/chain-sync.
            Task deliveries = Task.WhenAll(keepAliveRead, chainRead);
            Task completed = await Task.WhenAny(deliveries, Task.Delay(TimeSpan.FromSeconds(2), deadline.Token));
            Assert.Same(deliveries, completed);
            await deliveries;
            Assert.Equal(new byte[] { 2 }, (await keepAliveRead).ToArray());
            Assert.Equal(new byte[] { 3 }, (await chainRead).ToArray());
        }
        finally
        {
            release.Set();
            _ = asyncRelease.TrySetResult();
            await deadline.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(run, handler, keepAliveRead, chainRead).WaitAsync(Deadline));
        }

        async Task HandleAsync()
        {
            _ = await slow.ReadAsync(deadline.Token).ConfigureAwait(false);
            output.WriteLine("Slow handler on RunAsync stack: {0}; synchronous blocking: {1}",
                HasRunFrame<Demuxer>(), blocksSynchronously);
            entered.SetResult();
            if (blocksSynchronously)
            {
                Assert.True(release.Wait(Deadline), "Test cleanup did not release the slow handler");
            }
            else
            {
                await asyncRelease.Task.WaitAsync(deadline.Token);
            }
        }
    }

    [Fact]
    public async Task FullDemuxChannelPreservesOrderAndOwnedPayloadsAcrossReads()
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> reader = demuxer.Subscribe(ProtocolType.BlockFetch);
        // More than the 64-entry bound; each AdvanceTo poisons the borrowed
        // bearer memory, so retained results must be independently owned.
        byte[] frames = [.. Enumerable.Range(0, 160).SelectMany(index => Frame(ProtocolType.BlockFetch, [(byte)(index % 24)]))];
        Task run = Task.CompletedTask;
        List<ReadOnlyMemory<byte>> retained = [];
        try
        {
            _ = await bearer.Incoming.WriteAsync(frames, deadline.Token);
            await bearer.Incoming.CompleteAsync();
            run = demuxer.RunAsync(deadline.Token);
            Assert.False(run.IsCompleted, "The full channel must backpressure the demuxer");
            for (int index = 0; index < 160; index++)
            {
                retained.Add(await reader.ReadAsync(deadline.Token));
            }
            await run.WaitAsync(Deadline);
            Assert.True(bearer.ReaderAdvances >= 2);
            for (int index = 0; index < retained.Count; index++)
            {
                Assert.Equal((byte)(index % 24), Assert.Single(retained[index].ToArray()));
            }
        }
        finally
        {
            await deadline.CancelAsync();
            _ = await Record.ExceptionAsync(() => run.WaitAsync(Deadline));
        }
    }

    [Theory]
    [InlineData("enqueue")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task BearerFlushContinuationCanReenterOrStopTransport(string action)
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        byte[] payload = new byte[ushort.MaxValue];
        payload.AsSpan().Fill(0x9b);
        Task run = plexer.RunAsync(deadline.Token);
        Task send = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload));
        Task continuation = Task.CompletedTask;
        Task drain = Task.CompletedTask;
        try
        {
            Assert.False(send.IsCompleted);
            continuation = ContinueAsync();
            drain = DrainAsync(bearer.Outgoing, action == "enqueue" ? [payload.Length, 1] : [payload.Length], deadline.Token);
            await Task.WhenAll(continuation, drain).WaitAsync(Deadline);
            if (action == "dispose")
            {
                await run.WaitAsync(Deadline);
            }
            else
            {
                await deadline.CancelAsync();
                _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Deadline));
            }
            ConnectionClosedException stopped = await Assert.ThrowsAsync<ConnectionClosedException>(() =>
                channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([9])).WaitAsync(Deadline));
            if (action == "dispose") { _ = Assert.IsType<ObjectDisposedException>(stopped.InnerException); }
            else { _ = Assert.IsAssignableFrom<OperationCanceledException>(stopped.InnerException); }
        }
        finally
        {
            plexer.Dispose();
            await deadline.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(send, run, continuation, drain).WaitAsync(Deadline));
        }

        async Task ContinueAsync()
        {
            await send.ConfigureAwait(false);
            Assert.False(HasRunFrame<Demuxer>());
            // Reuse the caller's source immediately after commit; the wire payload must not change.
            payload.AsSpan().Fill(0xff);
            switch (action)
            {
                case "enqueue":
                    await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([0x9b]));
                    break;
                case "cancel":
                    await deadline.CancelAsync();
                    break;
                case "dispose":
                    plexer.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    [Fact]
    public async Task BackpressuredMuxedAndRawBatchWritesShareTheBearerLock()
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        byte[] payload = new byte[ushort.MaxValue];
        payload.AsSpan().Fill(0x9b);
        byte[] raw = Frame(ProtocolType.BlockFetch, [0x9b]);
        Task first = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload), deadline.Token);
        Task second = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload), deadline.Token);
        Task direct = channel.WriteRawSegmentAsync(raw, deadline.Token);
        Task batch = channel.WriteBatchRawSegmentsAsync(raw, 3, deadline.Token);
        Task drain = Task.CompletedTask;
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(direct.IsCompleted);
            Assert.False(batch.IsCompleted);
            drain = DrainAsync(bearer.Outgoing, [payload.Length, payload.Length, 1, 1, 1, 1], deadline.Token);
            await Task.WhenAll(first, second, direct, batch, drain).WaitAsync(Deadline);
        }
        finally
        {
            plexer.Dispose();
            await deadline.CancelAsync();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, second, direct, batch, drain).WaitAsync(Deadline));
        }
    }

    private static byte[] Frame(ProtocolType protocol, byte[] payload)
    {
        byte[] frame = new byte[ProtocolConstants.SegmentHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)protocol);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), checked((ushort)payload.Length));
        payload.CopyTo(frame, ProtocolConstants.SegmentHeaderSize);
        return frame;
    }

    private static bool HasRunFrame<T>() => new StackTrace().GetFrames().Any(frame =>
        (frame.GetMethod()?.DeclaringType == typeof(T) && frame.GetMethod()?.Name is "Turn" or "CallerTurn" or "Callback") ||
        (frame.GetMethod()?.DeclaringType?.DeclaringType == typeof(T) &&
         frame.GetMethod()?.DeclaringType?.Name.StartsWith("<RunAsync>", StringComparison.Ordinal) == true));

    private static async Task DrainAsync(PipeReader reader, int[] expectedPayloadLengths, CancellationToken token)
    {
        List<byte[]> payloads = [];
        while (payloads.Count < expectedPayloadLengths.Length)
        {
            ReadResult result = await reader.ReadAsync(token);
            ReadOnlySequence<byte> remaining = result.Buffer;
            while (remaining.Length >= ProtocolConstants.SegmentHeaderSize)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(remaining.Slice(0, ProtocolConstants.SegmentHeaderSize));
                long length = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                if (remaining.Length < length) { break; }
                Assert.Equal(ProtocolType.BlockFetch, header.ProtocolId);
                payloads.Add(remaining.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength).ToArray());
                remaining = remaining.Slice(length);
            }
            reader.AdvanceTo(remaining.Start, remaining.End);
            Assert.False(result.IsCompleted && payloads.Count < expectedPayloadLengths.Length, "Bearer completed before all frames arrived");
        }
        Assert.Equal(expectedPayloadLengths.Length, payloads.Count);
        Assert.Equal(expectedPayloadLengths.Order(), payloads.Select(payload => payload.Length).Order());
        Assert.All(payloads, payload => Assert.All(payload, value => Assert.Equal(0x9b, value)));
    }

    private sealed class TestBearer(PipeOptions? outgoingOptions = null) : IBearer
    {
        private readonly Pipe _incoming = new();
        private readonly Pipe _outgoing = new(outgoingOptions ?? PipeOptions.Default);
        private PoisoningReader? _reader;
        public PipeReader Reader => _reader ??= new PoisoningReader(_incoming.Reader);
        public PipeWriter Writer => _outgoing.Writer;
        public PipeWriter Incoming => _incoming.Writer;
        public PipeReader Outgoing => _outgoing.Reader;
        public int ReaderAdvances => _reader?.Advances ?? 0;

        public void Dispose()
        {
            _incoming.Reader.Complete();
            _incoming.Writer.Complete();
            _outgoing.Reader.Complete();
            _outgoing.Writer.Complete();
        }
    }

    private sealed class PoisoningReader(PipeReader reader) : PipeReader
    {
        private ReadOnlySequence<byte> _buffer;
        public int Advances { get; private set; }
        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            foreach (ReadOnlyMemory<byte> memory in _buffer.Slice(0, consumed))
            {
                Assert.True(MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment));
                segment.AsSpan().Fill(0xee);
            }
            reader.AdvanceTo(consumed, examined);
            Advances++;
        }
        public override void CancelPendingRead() => reader.CancelPendingRead();
        public override void Complete(Exception? exception = null) => reader.Complete(exception);
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            return LimitRead(result);
        }
        public override bool TryRead(out ReadResult result)
        {
            bool available = reader.TryRead(out result);
            if (available) { result = LimitRead(result); }
            return available;
        }
        private ReadResult LimitRead(ReadResult result)
        {
            // Force separate nonempty reads as well as buffer poisoning.
            _buffer = result.Buffer.Slice(0, Math.Min(result.Buffer.Length, 720));
            return new ReadResult(_buffer, result.IsCanceled, result.IsCompleted && _buffer.Length == result.Buffer.Length);
        }
    }
}
