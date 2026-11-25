using System.Buffers.Binary;
using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Test;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using SAIB.Cbor;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

public sealed class PooledReceivePerfTests
{
    private static readonly FieldInfo Staging = typeof(MessageAssembler).GetField("_staging", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Generation = typeof(ProtocolQueue).GetField("_generation", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void StagingGrowsAndReturnsWithoutAliasingOwnedMessages()
    {
        using MessageAssembler assembler = new();
        byte[] large = Block88KiB();
        Assert.True(assembler.Append(large.AsMemory(0, 32)).IsEmpty);
        byte[] smallRental = Assert.IsType<byte[]>(Staging.GetValue(assembler));
        Assert.True(assembler.Append(large.AsMemory(32, 65500)).IsEmpty);
        byte[] largeRental = Assert.IsType<byte[]>(Staging.GetValue(assembler));
        Assert.NotSame(smallRental, largeRental);
        ReadOnlyMemory<byte> owned = assembler.Append(large.AsMemory(65532));
        Assert.Null(Staging.GetValue(assembler));
        Assert.Equal(large, owned.ToArray());
        byte[] small = ReceiveVectors.Body(23);
        Assert.True(assembler.Append(small.AsMemory(0, 16)).IsEmpty);
        Assert.True(Assert.IsType<byte[]>(Staging.GetValue(assembler)).Length < largeRental.Length);
        Assert.Equal(small, assembler.Append(small.AsMemory(16)).ToArray());
        Assert.Equal(large, owned.ToArray());
        Assert.Null(Staging.GetValue(assembler));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseOnFailureOrDisposeIsIdempotent(bool failFirst)
    {
        using MessageAssembler assembler = new();
        Assert.True(assembler.Append(new byte[] { 0x81 }).IsEmpty);
        Assert.NotNull(Staging.GetValue(assembler));
        IOException error = new();
        if (failFirst) { _ = assembler.Fail(error); }
        else { assembler.Dispose(); }
        Assert.Null(Staging.GetValue(assembler));
        _ = assembler.Fail(error);
        assembler.Dispose();
        _ = assembler.Fail(error);
        Assert.Null(Staging.GetValue(assembler));
    }

    [Fact]
    public void ParkedEightSegmentBlockUsesOneWaitAndNoWaitAllocationOverDecode()
    {
        using Link link = new(ProtocolType.BlockFetch);
        using CancellationTokenSource cancel = new();
        byte[] wire = Block88KiB();
        BlockFetchMessage? last = null;

        void Receive()
        {
            ValueTask<BlockFetchMessage> pending = link.Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(cancel.Token);
            for (int i = 0; i < 7; i++) { link.DeliverItem(default); }
            link.DeliverItem(wire);
            last = pending.GetAwaiter().GetResult();
        }

        int before = link.Waits;
        Receive();
        Assert.Equal(1, link.Waits - before);
        Assert.Equal(wire, last!.Raw.ToArray());

        void Decode() => last = CborSerializer.Deserialize<BlockFetchMessage>(wire, out _);
        void DecodeBatch() { for (int i = 0; i < 100; i++) { Decode(); } }
        void ReceiveBatch() { for (int i = 0; i < 100; i++) { Receive(); } }
        void Verify() => Assert.Equal(wire.Length, last!.Raw.Length);
        Assert.Equal(AllocationAssert.Measure(DecodeBatch, DecodeBatch, Verify), AllocationAssert.Measure(ReceiveBatch, ReceiveBatch, Verify));
    }

    [Theory]
    [InlineData(65535, true)]
    [InlineData(65535, false)]
    [InlineData(33334, true)]
    [InlineData(33334, false)]
    public async Task LongMessageDrainsWithOrWithoutParkedReader(int chunk, bool parked)
    {
        using Link link = new(ProtocolType.LocalStateQuery);
        byte[] body = ReceiveVectors.Body(5000000);
        ReceiveSchedule schedule = OwnedReceiveTests.Frames(ProtocolType.LocalStateQuery, body, chunk);
        Assert.Equal(chunk == 65535 ? 77 : 150, (body.Length + chunk - 1) / chunk);
        Task<BlockFetchMessage>? pending = parked ? link.Receive() : null;
        link.Bearer.Deliver(schedule.Wire.ToArray(), end: true);
        Task run = link.Demuxer.RunAsync(CancellationToken.None);
        if (!parked)
        {
            await WaitFor(() => link.Demuxer.Stopped);
            Assert.Equal(64, link.Queue.Count);
        }
        Assert.Equal(body, (await (pending ?? link.Receive()).WaitAsync(Timeout)).Raw.ToArray());
        await run.WaitAsync(Timeout);
        if (parked) { Assert.Equal(1, link.Waits); }
        Assert.Equal(0, link.Queue.Count);
    }

    [Theory]
    [InlineData(64, 1)]
    [InlineData(8, 8)]
    public async Task BackpressureCountsSegmentsUntilAConsumerDrains(int messages, int segments)
    {
        using Link link = new(ProtocolType.BlockFetch);
        ProtocolQueue following = (ProtocolQueue)link.Demuxer.Subscribe(ProtocolType.KeepAlive);
        byte[] body = ReceiveVectors.Body(256);
        ReceiveSchedule schedule = new("backpressure");
        for (int i = 0; i < messages; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int start = j * body.Length / segments;
                schedule.Segment(ProtocolType.BlockFetch, body[start..((j + 1) * body.Length / segments)]);
            }
        }
        // The 65th segment must target the same queue to enter the stopped role.
        schedule.Segment(ProtocolType.BlockFetch, body);
        schedule.Segment(ProtocolType.KeepAlive, body);
        link.Bearer.Deliver(schedule.Wire.ToArray(), end: true);
        Task run = link.Demuxer.RunAsync(CancellationToken.None);
        await WaitFor(() => link.Demuxer.Stopped);
        Assert.Equal(64, link.Queue.Count);
        Assert.Equal(0, following.Count);
        for (int i = 0; i <= messages; i++) { Assert.Equal(body, (await link.Receive().WaitAsync(Timeout)).Raw.ToArray()); }
        Assert.Equal(body, (await following.ReadAsync().AsTask().WaitAsync(Timeout)).ToArray());
        await run.WaitAsync(Timeout);
    }

    public static TheoryData<bool, string> EndCases => new()
    {
        { false, "clean" }, { false, "partial" }, { false, "dispose" },
        { false, "cbor" }, { false, "cap" }, { false, "io" },
        { true, "clean" }, { true, "partial" }, { true, "dispose" },
        { true, "cbor" }, { true, "cap" }, { true, "io" }
    };


    [Theory]
    [MemberData(nameof(EndCases))]
    public async Task TerminalWakeupAfterTrailingEmptySegments(bool parked, string end)
    {
        using Link link = new(ProtocolType.BlockFetch);
        Task<BlockFetchMessage>? pending = parked ? link.Receive() : null;
        ReceiveSchedule schedule = new("terminal");
        schedule.Segment(ProtocolType.BlockFetch, []);
        schedule.Segment(ProtocolType.BlockFetch, end is "partial" or "io" or "dispose" ? [0x81] : []);
        link.Bearer.Deliver(schedule.Wire.ToArray(), end: end is "clean" or "partial");
        Task run = link.Demuxer.RunAsync(CancellationToken.None);
        if (end is not ("clean" or "partial"))
        {
            await WaitFor(() => link.Bearer.Consumed == schedule.Wire.Count && link.Bearer.Pending);
            if (end == "dispose") { link.Demuxer.Dispose(); }
            else if (end == "io") { link.Bearer.Fail(new IOException("read fault")); }
            else if (end == "cbor")
            {
                ReceiveSchedule invalid = new("invalid");
                invalid.Segment(ProtocolType.BlockFetch, [0xff]);
                link.Bearer.Deliver(invalid.Wire.ToArray());
            }
            else { link.Bearer.Deliver(OwnedReceiveTests.Frames(ProtocolType.BlockFetch, ReceiveVectors.Body(2500000)[..2_500_001]).Wire.ToArray()); }
        }
        Type? cause = end switch
        {
            "clean" => null,
            "partial" => typeof(EndOfStreamException),
            "dispose" => typeof(ObjectDisposedException),
            "cbor" => typeof(CborException),
            "cap" => typeof(InvalidDataException),
            _ => typeof(IOException)
        };
        if (end is "clean" or "dispose") { await run.WaitAsync(Timeout); }
        else { _ = await Assert.ThrowsAsync(cause!, () => run.WaitAsync(Timeout)); }
        ConnectionClosedException error = await Assert.ThrowsAsync<ConnectionClosedException>(() => (pending ?? link.Receive()).WaitAsync(Timeout));
        Assert.Equal(cause, error.InnerException?.GetType());
        Assert.Null(Staging.GetValue(link.Queue.Assembler));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterEmptiesKeepsNextItemAndRejectsConcurrentRead(bool parked)
    {
        using Link link = new(ProtocolType.BlockFetch);
        using CancellationTokenSource cancel = new();
        if (!parked) { await cancel.CancelAsync(); }
        Task<BlockFetchMessage>? pending = parked ? link.Receive(cancel.Token) : null;
        link.DeliverItem(default);
        if (parked)
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => link.Receive());
            _ = Assert.Throws<InvalidOperationException>(() => link.Queue.ReadAsync().AsTask().GetAwaiter().GetResult());
            await cancel.CancelAsync();
        }
        byte[] next = ReceiveVectors.Body();
        link.DeliverItem(next);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => (pending ?? link.Receive(cancel.Token)).WaitAsync(Timeout));
        Assert.Equal(next, (await link.Receive().WaitAsync(Timeout)).Raw.ToArray());
    }

    private static byte[] Block88KiB()
    {
        const int payloadBytes = 88 * 1024;
        byte[] block = ReceiveVectors.Body(5000000)[..(payloadBytes + 14)];
        BinaryPrimitives.WriteInt32BigEndian(block.AsSpan(5), payloadBytes + 5);
        BinaryPrimitives.WriteInt32BigEndian(block.AsSpan(10), payloadBytes);
        return block;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using CancellationTokenSource deadline = new(Timeout);
        while (!condition()) { await Task.Delay(1, deadline.Token); }
    }

    private sealed class Link : IDisposable
    {
        internal ScriptedReceiveBearer Bearer { get; } = new();
        internal Demuxer Demuxer { get; }
        internal ProtocolQueue Queue { get; }
        internal ChannelBuffer Buffer { get; }
        internal int Waits { get { lock (Demuxer.Gate) { return (int)Generation.GetValue(Queue)!; } } }

        internal Link(ProtocolType protocol)
        {
            Connection connection = new(Bearer);
            Demuxer = new(Bearer, connection);
            Queue = (ProtocolQueue)Demuxer.Subscribe(protocol);
            Buffer = new(new AgentChannel(protocol, Bearer.Writer, Queue, connection, ProtocolMode.Initiator));
        }

        internal Task<BlockFetchMessage> Receive(CancellationToken token = default) => Buffer.ReceiveFullMessageAsync<BlockFetchMessage>(token).AsTask();
        internal void DeliverItem(ReadOnlyMemory<byte> item)
        {
            ProtocolQueue.Delivery delivery;
            lock (Demuxer.Gate) { delivery = Queue.EnqueueLocked(item); }
            delivery.Complete(inline: true);
        }
        public void Dispose() { Demuxer.Dispose(); Bearer.Dispose(); }
    }
}
