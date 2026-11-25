using System.Reflection;
using SAIB.Cbor;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

public sealed class OwnedReceiveTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(23)]
    [InlineData(256)]
    public void EveryHeadStringAndNestedSplitYieldsOneOwnedItem(int size)
    {
        byte[] message = ReceiveVectors.Body(size);
        for (int split = 1; split < message.Length; split++)
        {
            using MessageAssembler assembler = new();
            Assert.True(assembler.Append(message.AsMemory(0, split)).IsEmpty);
            ReadOnlyMemory<byte> result = assembler.Append(message.AsMemory(split));
            Assert.Equal(message, result.ToArray());
            byte[] following = ReceiveVectors.Body(24);
            Assert.Equal(following, assembler.Append(following).ToArray());
            Assert.Equal(message, result.ToArray());
        }
    }

    [Fact]
    public void PackedMessagesEmptyItemsAndStagingReuse()
    {
        byte[] a = ReceiveVectors.Body(1000);
        byte[] b = ReceiveVectors.Body();
        using MessageAssembler assembler = new();
        Assert.True(assembler.Append(ReadOnlyMemory<byte>.Empty).IsEmpty);
        Assert.True(assembler.Append(a.AsMemory(0, 800)).IsEmpty);
        FieldInfo field = typeof(MessageAssembler).GetField("_staging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(field.GetValue(assembler));
        byte[] last = [.. a.AsSpan(800), .. b];
        ReadOnlyMemory<byte> first = assembler.Append(last);
        Assert.Null(field.GetValue(assembler));
        Assert.Equal(a.Concat(b), first.ToArray());
        for (int i = 0; i < 3; i++)
        {
            Assert.True(assembler.Append(a.AsMemory(0, 800)).IsEmpty);
            Assert.NotNull(field.GetValue(assembler));
            Assert.Equal(a, assembler.Append(a.AsMemory(800)).ToArray());
            Assert.Null(field.GetValue(assembler));
        }
        Assert.Equal(a.Concat(b), first.ToArray());
    }

    [Fact]
    public async Task CallerItemsAreSlicesButSpanningMessagesOwnAnArray()
    {
        Channel<ReadOnlyMemory<byte>> input = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        System.IO.Pipelines.Pipe unused = new();
        using SemaphoreSlim gate = new(1);
        AgentChannel channel = new(ProtocolType.BlockFetch, unused.Writer, input.Reader, gate);
        ChannelBuffer buffer = new(channel);
        byte[] a = ReceiveVectors.Body();
        byte[] b = ReceiveVectors.Body(256);
        byte[] packed = [.. a, .. a];
        await input.Writer.WriteAsync(packed);
        await input.Writer.WriteAsync(b.AsMemory(0, 13));
        BlockFetchMessage first = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        Assert.True(MemoryMarshal.TryGetArray(first.Raw, out ArraySegment<byte> slice));
        Assert.Same(packed, slice.Array);
        _ = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        using CancellationTokenSource cancel = new();
        Task<BlockFetchMessage> interrupted = buffer.ReceiveFullMessageAsync<BlockFetchMessage>(cancel.Token).AsTask();
        Assert.False(interrupted.IsCompleted);
        await cancel.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);
        await input.Writer.WriteAsync(b.AsMemory(13));
        input.Writer.Complete();
        BlockFetchMessage spanning = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None);
        Assert.True(MemoryMarshal.TryGetArray(spanning.Raw, out ArraySegment<byte> owned));
        Assert.NotSame(b, owned.Array);
        Assert.Equal(b, spanning.Raw.ToArray());
        Assert.Equal(a, first.Raw.ToArray());
        await unused.Writer.CompleteAsync();
        await unused.Reader.CompleteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerInvalidCborCannotFabricateAMessageAfterTheError(bool closeAfterError)
    {
        Channel<ReadOnlyMemory<byte>> input = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        System.IO.Pipelines.Pipe unused = new();
        using SemaphoreSlim gate = new(1);
        ChannelBuffer buffer = new(new AgentChannel(ProtocolType.BlockFetch, unused.Writer, input.Reader, gate));
        _ = input.Writer.TryWrite(new byte[] { 0x82, 0x04 });
        _ = input.Writer.TryWrite(new byte[] { 0xff });
        if (!closeAfterError) { _ = input.Writer.TryWrite(Convert.FromHexString("D8184100")); }
        input.Writer.Complete();
        _ = await Assert.ThrowsAsync<CborException>(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask());
        _ = await Assert.ThrowsAsync<CborException>(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask());
        await unused.Reader.CompleteAsync();
        await unused.Writer.CompleteAsync();
    }

    [Fact]
    public async Task CallerClosedPartialReturnsTheScannerSpill()
    {
        Channel<ReadOnlyMemory<byte>> input = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        System.IO.Pipelines.Pipe unused = new();
        using SemaphoreSlim gate = new(1);
        ChannelBuffer buffer = new(new AgentChannel(ProtocolType.BlockFetch, unused.Writer, input.Reader, gate));
        _ = input.Writer.TryWrite(Enumerable.Repeat((byte)0x81, 65).ToArray());
        input.Writer.Complete();
        _ = await Assert.ThrowsAsync<ChannelClosedException>(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask());
        object assembler = typeof(ChannelBuffer).GetField("_assembler", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(buffer)!;
        Assert.Null(typeof(MessageAssembler).GetField("_staging", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(assembler));
        object scanner = typeof(MessageAssembler).GetField("_scanner", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(assembler)!;
        Assert.Null(typeof(CborFrameScanner.State).GetField("_spill", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scanner));
        await unused.Reader.CompleteAsync();
        await unused.Writer.CompleteAsync();
    }

    [Fact]
    public async Task CallerReadFailurePreservesThePartialScanner()
    {
        Channel<ReadOnlyMemory<byte>> input = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        System.IO.Pipelines.Pipe unused = new();
        using SemaphoreSlim gate = new(1);
        ChannelBuffer buffer = new(new AgentChannel(ProtocolType.BlockFetch, unused.Writer, new FailSecondRead(input.Reader), gate));
        byte[] message = ReceiveVectors.Body(256);
        _ = input.Writer.TryWrite(message.AsMemory(0, 13));
        _ = input.Writer.TryWrite(message.AsMemory(13));
        input.Writer.Complete();
        _ = await Assert.ThrowsAsync<IOException>(() => buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None).AsTask());
        Assert.Equal(message, (await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(CancellationToken.None)).Raw.ToArray());
        await unused.Reader.CompleteAsync();
        await unused.Writer.CompleteAsync();
    }

    private sealed class FailSecondRead(ChannelReader<ReadOnlyMemory<byte>> inner) : ChannelReader<ReadOnlyMemory<byte>>
    {
        private int _reads;
        public override bool TryRead(out ReadOnlyMemory<byte> item) => inner.TryRead(out item);
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => inner.WaitToReadAsync(cancellationToken);
        public override ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default) => ++_reads == 2
            ? ValueTask.FromException<ReadOnlyMemory<byte>>(new IOException("scripted caller read fault")) : inner.ReadAsync(cancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblerFailureIsSticky(bool cap)
    {
        using MessageAssembler assembler = new(cap ? 2 : null);
        Assert.True(assembler.Append(new byte[] { 0x82, 0x04 }).IsEmpty);
        byte[] invalid = cap ? Convert.FromHexString("D8184100") : [0xff];
        Exception error = Assert.ThrowsAny<Exception>(() => assembler.Append(invalid));
        Assert.Equal(cap ? typeof(InvalidDataException) : typeof(CborException), error.GetType());
        Assert.Same(error, Assert.ThrowsAny<Exception>(() => assembler.Append(new byte[] { 0 })));
    }

    [Fact]
    public async Task ExactN2NCapPasses()
    {
        // A definite byte string: five-byte head plus 2,499,995 body bytes = exactly the cap.
        byte[] message = new byte[2_500_000];
        Convert.FromHexString("5A0026259B").CopyTo(message, 0);
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> reader = demuxer.Subscribe(ProtocolType.BlockFetch);
        bearer.Deliver(Frames(ProtocolType.BlockFetch, message).Wire.ToArray(), end: true);
        Task run = demuxer.RunAsync(CancellationToken.None);
        ReadOnlyMemory<byte> item;
        do { item = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); } while (item.IsEmpty);
        Assert.Equal(message, item.ToArray());
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ProtocolLimitTableCoversEveryProtocol()
    {
        foreach (ProtocolType protocol in Enum.GetValues<ProtocolType>())
        {
            int? cap = ProtocolLimits.Get(protocol);
            Assert.Equal(protocol is ProtocolType.ClientChainSync or ProtocolType.LocalTxSubmission or ProtocolType.LocalStateQuery or ProtocolType.LocalTxMonitor ? null : 2_500_000, cap);
        }
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolLimits.Get((ProtocolType)99));
    }

    [Theory]
    [InlineData(ProtocolType.Handshake)]
    [InlineData(ProtocolType.NodeChainSync)]
    [InlineData(ProtocolType.BlockFetch)]
    [InlineData(ProtocolType.TxSubmission)]
    [InlineData(ProtocolType.KeepAlive)]
    [InlineData(ProtocolType.PeerSharing)]
    public async Task OversizedN2NMessageEndsEveryProtocol(ProtocolType protocol)
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> target = demuxer.Subscribe(protocol);
        ChannelReader<ReadOnlyMemory<byte>> other = demuxer.Subscribe(ProtocolType.ClientChainSync);
        // The independently generated 2.5 MB vector is still incomplete at byte 2,500,001.
        byte[] message = ReceiveVectors.Body(2500000)[..2_500_001];
        ReceiveSchedule schedule = Frames(protocol, message);
        bearer.Deliver(schedule.Wire.ToArray());
        Task run = demuxer.RunAsync(CancellationToken.None);
        _ = await Assert.ThrowsAsync<InvalidDataException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        while (target.TryRead(out ReadOnlyMemory<byte> item)) { Assert.True(item.IsEmpty); }
        ConnectionClosedException end = await Assert.ThrowsAsync<ConnectionClosedException>(() => target.ReadAsync().AsTask());
        _ = Assert.IsType<InvalidDataException>(end.InnerException);
        end = await Assert.ThrowsAsync<ConnectionClosedException>(() => other.ReadAsync().AsTask());
        _ = Assert.IsType<InvalidDataException>(end.InnerException);
    }

    [Fact]
    public async Task FiveMegabyteN2CReplyAndFollowingProtocolPassWithoutAReaderStall()
    {
        using ScriptedReceiveBearer bearer = new();
        using Demuxer demuxer = new(bearer);
        ChannelReader<ReadOnlyMemory<byte>> local = demuxer.Subscribe(ProtocolType.LocalStateQuery);
        ChannelReader<ReadOnlyMemory<byte>> keep = demuxer.Subscribe(ProtocolType.KeepAlive);
        byte[] message = ReceiveVectors.Body(5000000);
        ReceiveSchedule schedule = Frames(ProtocolType.LocalStateQuery, message);
        schedule.Segment(ProtocolType.KeepAlive, ReceiveVectors.Body());
        bearer.Deliver(schedule.Wire.ToArray(), end: true);
        Task run = demuxer.RunAsync(CancellationToken.None);
        ReadOnlyMemory<byte> result;
        do { result = await local.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); } while (result.IsEmpty);
        Assert.Equal(message, result.ToArray());
        Assert.Equal(ReceiveVectors.Body(), (await keep.ReadAsync()).ToArray());
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    internal static ReceiveSchedule Frames(ProtocolType protocol, byte[] message, int chunk = 65535)
    {
        ReceiveSchedule schedule = new("risk");
        for (int offset = 0; offset < message.Length; offset += chunk)
        {
            schedule.Segment(protocol, message.AsSpan(offset, Math.Min(chunk, message.Length - offset)).ToArray());
        }
        return schedule;
    }
}
