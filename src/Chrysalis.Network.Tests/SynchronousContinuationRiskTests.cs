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
    private const string StoppedMessage = "The muxer has stopped accepting protocol messages.";

    [Theory]
    [InlineData("send and subscribe")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task DemuxContinuationCanReenterTransport(string action)
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        using SemaphoreSlim bearerLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerLock);
        AgentChannel channel = new(ProtocolType.BlockFetch, muxer.Writer, bearer.Writer,
            demuxer.Subscribe(ProtocolType.BlockFetch), bearerLock);
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
            _ = await Assert.ThrowsAsync<ChannelClosedException>(() => channel.ReadSegmentAsync().AsTask().WaitAsync(Deadline));
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
        byte[] frames = [.. Enumerable.Range(0, 160).SelectMany(index => Frame(ProtocolType.BlockFetch, [(byte)index]))];
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
                Assert.Equal((byte)index, Assert.Single(retained[index].ToArray()));
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
    public async Task MuxFlushContinuationCanReenterOrStopTransport(string action)
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new();
        using SemaphoreSlim bearerLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerLock);
        AgentChannel channel = ChannelFor(muxer, bearer, bearerLock);
        byte[] payload = new byte[65_533];
        payload.AsSpan().Fill(0x9b);
        Task send = Task.CompletedTask;
        Task outside = Task.CompletedTask;
        Task continuation = Task.CompletedTask;
        Task run = Task.CompletedTask;
        Task drain = Task.CompletedTask;
        try
        {
            send = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload));
            Assert.False(send.IsCompleted);
            continuation = ContinueAsync();
            run = Task.Run(() => muxer.RunAsync(deadline.Token), CancellationToken.None);
            drain = DrainAsync(bearer.Outgoing, action == "enqueue" ? [payload.Length, 1] : [payload.Length], deadline.Token);
            await Task.WhenAll(continuation, drain).WaitAsync(Deadline);
            await outside.WaitAsync(Deadline);
            if (action == "enqueue")
            {
                await deadline.CancelAsync();
            }
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Deadline));
            InvalidOperationException stopped = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([9])).WaitAsync(Deadline));
            Assert.Equal(StoppedMessage, stopped.Message);
            if (action == "dispose")
            {
                _ = Assert.IsType<ObjectDisposedException>(stopped.InnerException);
            }
            else
            {
                _ = Assert.IsAssignableFrom<OperationCanceledException>(stopped.InnerException);
            }
        }
        finally
        {
            await deadline.CancelAsync();
            muxer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(send, run, continuation, drain, outside).WaitAsync(Deadline));
        }

        async Task ContinueAsync()
        {
            await send.ConfigureAwait(false);
            output.WriteLine("Mux sender continuation on RunAsync stack: {0}", HasRunFrame<Muxer>());
            // Reuse the caller's source immediately after commit. Mux output
            // must still contain the original bytes while it drains.
            payload.AsSpan().Fill(0xff);
            switch (action)
            {
                case "enqueue":
                    await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>([0x9b]));
                    break;
                case "cancel":
                    outside = Task.Run(deadline.Cancel, CancellationToken.None);
                    await deadline.CancelAsync();
                    break;
                case "dispose":
                    outside = Task.Run(muxer.Dispose, CancellationToken.None);
                    muxer.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    [Fact]
    public async Task BackpressuredMuxAndRawBatchWritesDoNotInvertBearerLock()
    {
        using CancellationTokenSource deadline = new(Deadline);
        using TestBearer bearer = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        using SemaphoreSlim bearerLock = new(0, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerLock);
        AgentChannel channel = ChannelFor(muxer, bearer, bearerLock);
        byte[] payload = new byte[65_533];
        payload.AsSpan().Fill(0x9b);
        Task first = Task.CompletedTask;
        Task second = Task.CompletedTask;
        Task run = Task.CompletedTask;
        Task direct = Task.CompletedTask;
        Task batch = Task.CompletedTask;
        Task drain = Task.CompletedTask;
        byte[] raw = Frame(ProtocolType.BlockFetch, [0x9b]);
        try
        {
            first = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload));
            second = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload));
            run = muxer.RunAsync(deadline.Token);
            direct = channel.WriteRawSegmentAsync(raw, deadline.Token);
            batch = channel.WriteBatchRawSegmentsAsync(raw, 3, deadline.Token);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(direct.IsCompleted);
            Assert.False(batch.IsCompleted);
            _ = bearerLock.Release();
            drain = DrainAsync(bearer.Outgoing, [payload.Length, payload.Length, 1, 1, 1, 1], deadline.Token);
            await Task.WhenAll(first, second, direct, batch, drain).WaitAsync(Deadline);
        }
        finally
        {
            await deadline.CancelAsync();
            muxer.Dispose();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, second, direct, batch, drain, run).WaitAsync(Deadline));
        }
    }

    private static AgentChannel ChannelFor(Muxer muxer, TestBearer bearer, SemaphoreSlim bearerLock) => new(
        ProtocolType.BlockFetch, muxer.Writer, bearer.Writer,
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, bearerLock);

    private static byte[] Frame(ProtocolType protocol, byte[] payload)
    {
        byte[] frame = new byte[ProtocolConstants.SegmentHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)protocol);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), checked((ushort)payload.Length));
        payload.CopyTo(frame, ProtocolConstants.SegmentHeaderSize);
        return frame;
    }

    private static bool HasRunFrame<T>() => new StackTrace().GetFrames().Any(frame =>
        frame.GetMethod()?.DeclaringType?.DeclaringType == typeof(T)
        && frame.GetMethod()?.DeclaringType?.Name.StartsWith("<RunAsync>", StringComparison.Ordinal) == true);

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
