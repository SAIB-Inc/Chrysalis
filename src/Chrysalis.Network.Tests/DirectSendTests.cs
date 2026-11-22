using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public sealed class DirectSendTests
{
    private static readonly byte[] Payload = [0x82, 0x01, 0x02];

    [Theory]
    [InlineData(false, 0x00)]
    [InlineData(true, 0x80)]
    public async Task MuxedSendCommitsOneWireFrameOnTheCallersThread(bool responder, byte direction)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel channel = responder ? plexer.SubscribeServer(ProtocolType.KeepAlive) : plexer.SubscribeClient(ProtocolType.KeepAlive);
        int caller = Environment.CurrentManagedThreadId;
        Task send = channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload));
        Assert.True(send.IsCompletedSuccessfully);
        Assert.Equal(caller, bearer.Output.CommitThread);
        Assert.Equal(new byte[] { direction, 8, 0, 3, 0x82, 1, 2 }, bearer.Output.Bytes[4..]);
        Assert.Equal(1, bearer.Output.Flushes);
        await send;
    }

    [Fact]
    public async Task StandaloneResponderModeSetsTheWireHeaderBit()
    {
        SendWriter writer = new();
        using SemaphoreSlim gate = new(1, 1);
        AgentChannel channel = new(ProtocolType.KeepAlive, writer, Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, gate, ProtocolMode.Responder);

        await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload));
        Assert.Equal((ushort)(0x8000 | (ushort)ProtocolType.KeepAlive), BinaryPrimitives.ReadUInt16BigEndian(writer.Bytes.AsSpan(4)));
    }

    [Theory]
    [InlineData("bearerWriter")]
    [InlineData("demuxReader")]
    [InlineData("bearerWriteLock")]
    public async Task StandaloneConstructorRejectsNullBeforeItCanAffectSiblingSends(string parameter)
    {
        SendWriter writer = new();
        using SemaphoreSlim gate = new(1, 1);
        ChannelReader<ReadOnlyMemory<byte>> reader = Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader;
        AgentChannel sibling = new(ProtocolType.KeepAlive, writer, reader, gate);

        ArgumentNullException rejected = Assert.Throws<ArgumentNullException>(() => new AgentChannel(ProtocolType.BlockFetch,
            parameter == "bearerWriter" ? null! : writer,
            parameter == "demuxReader" ? null! : reader,
            parameter == "bearerWriteLock" ? null! : gate));
        Assert.Equal(parameter, rejected.ParamName);
        await sibling.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload));
        Assert.Equal(Payload, writer.Bytes[ProtocolConstants.SegmentHeaderSize..]);
        Assert.Equal(1, writer.Flushes);
    }

    [Fact]
    public async Task OversizedPayloadIsRejectedBeforeAnyCommit()
    {
        const int oversized = ProtocolConstants.MaxSegmentPayloadLength + 1;
        // The writer can fit the oversized frame, so a capacity exception cannot mask a missing guard.
        using SendBearer bearer = new(new SendWriter(capacity: ProtocolConstants.SegmentHeaderSize + oversized));
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
        ArgumentOutOfRangeException rejected = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(new byte[oversized])));
        Assert.Equal("chunk", rejected.ParamName);
        Assert.Empty(bearer.Output.Bytes);
        Assert.Equal(0, bearer.Output.Flushes);
    }

    [Fact]
    public async Task BatchCancellationDuringFirstAdvanceStillCommitsEveryCopyWithOneFlush()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        using CancellationTokenSource stop = new();
        bearer.Output.OnAdvance = stop.Cancel;
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
        await channel.WriteBatchRawSegmentsAsync(Payload, 3, stop.Token);
        Assert.True(stop.IsCancellationRequested);
        Assert.Equal(Payload.Concat(Payload).Concat(Payload), bearer.Output.Bytes);
        Assert.Equal(1, bearer.Output.Flushes);
        Assert.Equal(CancellationToken.None, bearer.Output.FlushToken);
    }

    [Fact]
    public async Task BatchFlushesOnceEvenWithoutCancellation()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        await plexer.SubscribeClient(ProtocolType.KeepAlive).WriteBatchRawSegmentsAsync(Payload, 3);
        Assert.Equal(1, bearer.Output.Flushes);
        Assert.Equal(3 * Payload.Length, bearer.Output.Bytes.Length);
    }

    [Fact]
    public async Task ClosingDuringFirstBatchCopyDoesNotAdvanceTheRemainingCopies()
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.KeepAlive);
        bearer.Output.OnAdvance = plexer.Dispose;

        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.WriteBatchRawSegmentsAsync(Payload, 3));
        Assert.Equal(Payload, bearer.Output.Bytes);
        Assert.Equal(0, bearer.Output.Flushes);
    }

    [Theory]
    [InlineData(((ProtocolConstants.MaxSegmentPayloadLength + ProtocolConstants.SegmentHeaderSize) / 3) + 1)]
    [InlineData(int.MaxValue)]
    public async Task OversizedBatchIsRejectedBeforeItsFirstCommit(int count)
    {
        using SemaphoreSlim gate = new(1, 1);
        SendWriter writer = new(capacity: Payload.Length)
        {
            OnAdvance = static () => throw new InvalidOperationException("Over-limit batch reached Advance.")
        };
        AgentChannel channel = new(ProtocolType.KeepAlive, writer, Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, gate);

        ArgumentOutOfRangeException rejected = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => channel.WriteBatchRawSegmentsAsync(Payload, count));
        Assert.Equal(nameof(count), rejected.ParamName);
        Assert.Empty(writer.Bytes);
        Assert.Equal(0, writer.Flushes);

        writer.OnAdvance = null;
        await channel.WriteRawSegmentAsync(Payload);
        Assert.Equal(Payload, writer.Bytes);
        Assert.Equal(1, writer.Flushes);
    }

    [Theory]
    [InlineData(6_554, true)]
    [InlineData(6_555, false)]
    public async Task ChainSyncNextRequestBatchHonorsTheWireSizeBoundary(int count, bool accepted)
    {
        // The network tool's --pipeline option refuses what this method refuses; its other parsed options are given, not taken from the environment.
        Assert.Equal(accepted, Program.TryParseOptions(["chainsync", "--tcp-port", "3001", "--magic", "2", "--keepalive", "20", "--blocks", "100", "--pipeline", $"{count}"], out _, out string? error));
        Assert.Equal(accepted ? null : $"Invalid pipeline depth value '{count}'. The maximum is 6554.", error);
        // Provide room for the rejected case, so only the production limit can reject it.
        using SendBearer bearer = new(new SendWriter(capacity: 65_550));
        using Plexer plexer = new(bearer);
        SendWriter writer = bearer.Output;
        ChainSync sync = new(plexer.SubscribeServer(ProtocolType.NodeChainSync), ProtocolType.NodeChainSync);

        if (accepted)
        {
            await sync.SendNextRequestBatchAsync(count, CancellationToken.None);
            byte[] bytes = writer.Bytes;
            Assert.Equal(65_540, bytes.Length);
            Assert.Equal((ushort)(0x8000 | (ushort)ProtocolType.NodeChainSync), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)));
            Assert.Equal(1, writer.Flushes);
        }
        else
        {
            ArgumentOutOfRangeException rejected = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => sync.SendNextRequestBatchAsync(count, CancellationToken.None));
            Assert.Equal(nameof(count), rejected.ParamName);
            Assert.Empty(writer.Bytes);
            Assert.Equal(0, writer.Flushes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadablePayloadFailsOnlyItsCaller(bool raw)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        ObjectDisposedException failure = new("caller payload");
        using UnreadableMemory memory = new(failure);
        AgentChannel sender = plexer.SubscribeClient(ProtocolType.BlockFetch);
        AgentChannel sibling = plexer.SubscribeClient(ProtocolType.KeepAlive);

        ObjectDisposedException seen = await Assert.ThrowsAsync<ObjectDisposedException>(() => raw
            ? sender.WriteRawSegmentAsync(memory.Buffer)
            : sender.EnqueueChunkAsync(new ReadOnlySequence<byte>(memory.Buffer)));
        Assert.Same(failure, seen);
        Assert.Empty(bearer.Output.Bytes);
        Assert.Equal(0, bearer.Output.Flushes);

        await sibling.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload));
        Assert.Equal(Payload, bearer.Output.Bytes[ProtocolConstants.SegmentHeaderSize..]);
        Assert.Equal(1, bearer.Output.Flushes);
    }

    [Fact]
    public async Task StandaloneChannelsOnOneLockShareTheFirstSendFailure()
    {
        using SemaphoreSlim gate = new(1, 1);
        using SemaphoreSlim otherGate = new(1, 1);
        ChannelReader<ReadOnlyMemory<byte>> reader = Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader;
        IOException failure = new("standalone bearer failure");
        SendWriter writer = new() { Flush = () => throw failure };
        AgentChannel first = new(ProtocolType.BlockFetch, writer, reader, gate);
        AgentChannel sibling = new(ProtocolType.KeepAlive, writer, reader, gate, ProtocolMode.Responder);
        AgentChannel independent = new(ProtocolType.KeepAlive, new SendWriter(), reader, otherGate);
        ConnectionClosedException initial = await Assert.ThrowsAsync<ConnectionClosedException>(() => first.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload)));
        ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => sibling.WriteRawSegmentAsync(Payload));
        Assert.Same(failure, initial.InnerException);
        Assert.Same(failure, later.InnerException);
        Assert.Equal(1, writer.Flushes);
        await independent.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload));
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("completed")]
    [InlineData("throws")]
    [InlineData("double-fault")]
    public async Task SendFaultEndsPendingReceiveAndPreservesTheCause(string fault)
    {
        using SendBearer bearer = new();
        using Plexer plexer = new(bearer);
        IOException writerFault = new("scripted transport failure");
        bearer.Output.Flush = fault switch
        {
            "cancelled" => static () => new FlushResult(isCanceled: true, isCompleted: false),
            "completed" => static () => new FlushResult(isCanceled: false, isCompleted: true),
            _ => () => throw writerFault
        };
        if (fault == "double-fault")
        {
            bearer.Output.OnCancelPendingFlush = static () => throw new IOException("secondary cleanup error");
        }
        AgentChannel receiver = plexer.SubscribeClient(ProtocolType.BlockFetch);
        AgentChannel sender = plexer.SubscribeClient(ProtocolType.KeepAlive);
        Task<ReadOnlyMemory<byte>> read = receiver.ReadSegmentAsync().AsTask();
        Assert.False(read.IsCompleted);
        try
        {
            ConnectionClosedException sent = await Assert.ThrowsAsync<ConnectionClosedException>(() => sender.WriteRawSegmentAsync(Payload));
            ChannelClosedException ended = await Assert.ThrowsAsync<ConnectionClosedException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(sent.InnerException);
            Assert.Same(sent.InnerException, ended.InnerException);
            ConnectionClosedException later = await Assert.ThrowsAsync<ConnectionClosedException>(() => receiver.EnqueueChunkAsync(new ReadOnlySequence<byte>(Payload)));
            Assert.Same(ended.InnerException, later.InnerException);
            if (fault is "throws" or "double-fault")
            {
                Assert.Same(writerFault, sent.InnerException);
                Assert.Same(writerFault, ended.InnerException);
                Assert.Same(writerFault, later.InnerException);
            }
            Assert.Equal(1, bearer.Output.Flushes);
            plexer.Dispose();
            Assert.True(bearer.IsDisposed);
        }
        finally
        {
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    private sealed class UnreadableMemory(ObjectDisposedException failure) : MemoryManager<byte>
    {
        internal Memory<byte> Buffer => CreateMemory(Payload.Length);
        public override Span<byte> GetSpan() => throw failure;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}

internal sealed class SendBearer(SendWriter? writer = null) : IBearer
{
    private readonly Pipe _input = new();
    public PipeReader Reader => _input.Reader;
    public PipeWriter Writer => Output;
    internal SendWriter Output { get; } = writer ?? new();
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        _input.Writer.Complete();
        _input.Reader.Complete();
        IsDisposed = true;
    }
}

internal sealed class SendWriter(int capacity = ProtocolConstants.MaxSegmentPayloadLength + ProtocolConstants.SegmentHeaderSize) : PipeWriter
{
    private readonly byte[] _buffer = new byte[capacity];
    private int _length;
    internal byte[] Bytes => _buffer[.._length];
    internal int Flushes { get; private set; }
    internal int CommitThread { get; private set; }
    internal CancellationToken FlushToken { get; private set; }
    internal Action? OnAdvance { get; set; }
    internal Action? OnCancelPendingFlush { get; set; }
    internal Func<ValueTask<FlushResult>>? AsyncFlush { get; set; }
    internal Func<FlushResult>? Flush { get; set; }

    public override void Advance(int bytes)
    {
        CommitThread = Environment.CurrentManagedThreadId;
        _length += bytes;
        OnAdvance?.Invoke();
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _buffer.AsMemory(_length);
    public override Span<byte> GetSpan(int sizeHint = 0) => _buffer.AsSpan(_length);
    public override void CancelPendingFlush() => OnCancelPendingFlush?.Invoke();
    public override void Complete(Exception? exception = null) { }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        Flushes++;
        FlushToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        return AsyncFlush?.Invoke() ?? new ValueTask<FlushResult>(Flush?.Invoke() ?? default);
    }
}
