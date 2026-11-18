using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class DirectWriteSerialisationTests
{
    private const int WriterCount = 8;
    private const int MessagesPerWriter = 2_000;
    private const int PayloadLength = 125;

    public static TheoryData<int> StressIterations => [.. Enumerable.Range(0, 50)];

    [Theory]
    [MemberData(nameof(StressIterations))]
    public async Task ConcurrentChannelsPreserveEveryFrameAndProtocolOrder(int iteration)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
        using LoopbackBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel[] channels = [.. Enumerable.Range(0, WriterCount)
            .Select(index => plexer.SubscribeClient((ProtocolType)(index + 2)))];
        await AssertTrafficAsync(channels, bearer.Outgoing, iteration, deadline);
    }

    [Fact]
    public async Task PublicConstructorChannelsShareTheWritersLock()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
        using LoopbackBearer bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        AgentChannel[] channels = [.. Enumerable.Range(0, WriterCount).Select(index =>
            new AgentChannel((ProtocolType)(index + 2), bearer.Writer,
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, bearerWriteLock))];
        await AssertTrafficAsync(channels, bearer.Outgoing, 50, deadline);
    }

    [Fact]
    public async Task DifferentWritersCanFlushIndependently()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        Pipe blocked = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        Pipe independent = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        ChannelReader<ReadOnlyMemory<byte>> reader = Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader;
        using SemaphoreSlim independentLock = new(1, 1);
        AgentChannel first = new(ProtocolType.BlockFetch, blocked.Writer, reader, bearerWriteLock);
        AgentChannel second = new(ProtocolType.KeepAlive, independent.Writer, reader, independentLock);
        ReadOnlySequence<byte> payload = new(new byte[PayloadLength]);
        Task pending = first.EnqueueChunkAsync(payload, deadline.Token);
        try
        {
            Assert.False(pending.IsCompleted);
            Task otherWriter = second.EnqueueChunkAsync(payload, deadline.Token);
            Assert.True(otherWriter.IsCompletedSuccessfully, "An unrelated writer must not wait for the blocked writer's lock");
            await otherWriter;
            ReadResult buffered = await blocked.Reader.ReadAsync(deadline.Token);
            blocked.Reader.AdvanceTo(buffered.Buffer.End);
            await pending.WaitAsync(deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            await blocked.Reader.CompleteAsync();
            _ = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await blocked.Writer.CompleteAsync();
            await independent.Writer.CompleteAsync();
            await independent.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task MuxedAndRawFramesRemainWholeAcrossBackpressure()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using LoopbackBearer bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        AgentChannel channel = new(ProtocolType.BlockFetch, bearer.Writer,
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Reader, bearerWriteLock);
        // A maximum-sized payload plus its wire header exceeds the default 64 KiB threshold.
        ReadOnlySequence<byte> payload = new(new byte[ushort.MaxValue]);
        Task first = channel.EnqueueChunkAsync(payload, deadline.Token);
        Assert.False(first.IsCompleted);
        Task second = channel.EnqueueChunkAsync(payload, deadline.Token);
        Assert.False(second.IsCompleted);
        byte[] raw = MuxSegmentCodec.Encode(new MuxSegment(
            new MuxSegmentHeader(0, ProtocolType.KeepAlive, PayloadLength, false),
            new ReadOnlySequence<byte>(new byte[PayloadLength]))).ToArray();
        Task fastPath = channel.WriteRawSegmentAsync(raw, deadline.Token);
        Assert.False(fastPath.IsCompleted);
        Task drain = DrainBackpressureFramesAsync(bearer.Outgoing, deadline.Token);
        try
        {
            await Task.WhenAll(first, second, fastPath, drain).WaitAsync(deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            bearer.Writer.CancelPendingFlush();
            _ = await Record.ExceptionAsync(() => Task.WhenAll(first, second, fastPath, drain).WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static async Task DrainBackpressureFramesAsync(PipeReader reader, CancellationToken token)
    {
        int muxed = 0;
        int raw = 0;
        while (muxed + raw < 3)
        {
            ReadResult result = await reader.ReadAsync(token);
            ReadOnlySequence<byte> remaining = result.Buffer;
            while (remaining.Length >= ProtocolConstants.SegmentHeaderSize)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(remaining.Slice(0, ProtocolConstants.SegmentHeaderSize));
                long size = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                if (remaining.Length < size) { break; }
                if (header.ProtocolId == ProtocolType.BlockFetch)
                {
                    Assert.Equal(ushort.MaxValue, header.PayloadLength);
                    muxed++;
                }
                else
                {
                    Assert.Equal(ProtocolType.KeepAlive, header.ProtocolId);
                    Assert.Equal(PayloadLength, header.PayloadLength);
                    raw++;
                }
                Assert.All(remaining.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength).ToArray(), value => Assert.Equal(0, value));
                remaining = remaining.Slice(size);
            }
            reader.AdvanceTo(remaining.Start, remaining.End);
        }
        Assert.Equal(2, muxed);
        Assert.Equal(1, raw);
    }

    private static async Task AssertTrafficAsync(AgentChannel[] channels, PipeReader outgoing,
        int iteration, CancellationTokenSource deadline)
    {
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] writers = [.. channels.Select((channel, writer) => Task.Run(async () =>
        {
            await start.Task.WaitAsync(deadline.Token);
            byte[] payload = new byte[PayloadLength];
            for (int sequence = 0; sequence < MessagesPerWriter; sequence++)
            {
                FillPayload(payload, channel.ProtocolId, writer, sequence, iteration);
                await channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload), deadline.Token);
            }
        }, deadline.Token))];
        Task read = ReadFramesAsync(outgoing, iteration, deadline.Token);
        Task traffic = Task.WhenAll(writers.Append(read));
        start.SetResult();

        try
        {
            await traffic;
        }
        finally
        {
            await deadline.CancelAsync();
            outgoing.CancelPendingRead();
            _ = await Record.ExceptionAsync(() => traffic.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static async Task ReadFramesAsync(PipeReader reader, int iteration, CancellationToken token)
    {
        int[] sequences = new int[WriterCount];
        byte[] expected = new byte[PayloadLength];
        int frames = 0;
        while (frames < WriterCount * MessagesPerWriter)
        {
            ReadResult result = await reader.ReadAsync(token);
            ReadOnlySequence<byte> remaining = result.Buffer;
            while (remaining.Length >= ProtocolConstants.SegmentHeaderSize)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(remaining.Slice(0, ProtocolConstants.SegmentHeaderSize));
                Assert.False(header.Mode);
                Assert.Equal(PayloadLength, header.PayloadLength);
                int writer = (int)header.ProtocolId - 2;
                Assert.InRange(writer, 0, WriterCount - 1);
                long frameLength = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                if (remaining.Length < frameLength)
                {
                    break;
                }

                Assert.InRange(sequences[writer], 0, MessagesPerWriter - 1);
                FillPayload(expected, header.ProtocolId, writer, sequences[writer], iteration);
                Assert.True(remaining.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength)
                    .ToArray().AsSpan().SequenceEqual(expected),
                    $"Payload mismatch: iteration {iteration}, writer {writer}, sequence {sequences[writer]}");
                sequences[writer]++;
                frames++;
                remaining = remaining.Slice(frameLength);
            }

            reader.AdvanceTo(remaining.Start, remaining.End);
            Assert.False(result.IsCompleted && frames < WriterCount * MessagesPerWriter,
                $"Bearer ended after {frames} frames in iteration {iteration}");
        }

        Assert.All(sequences, count => Assert.Equal(MessagesPerWriter, count));
    }

    private static void FillPayload(Span<byte> payload, ProtocolType protocol, int writer, int sequence, int iteration)
    {
        BinaryPrimitives.WriteInt32BigEndian(payload, (int)protocol);
        BinaryPrimitives.WriteInt32BigEndian(payload[4..], writer);
        BinaryPrimitives.WriteInt32BigEndian(payload[8..], sequence);
        BinaryPrimitives.WriteInt32BigEndian(payload[12..], iteration);
        uint checksum = 2166136261;
        for (int index = 0; index < payload.Length - sizeof(uint); index++)
        {
            if (index >= 16)
            {
                payload[index] = (byte)((writer * 31) + (sequence * 17) + iteration + index);
            }
            checksum = unchecked((checksum ^ payload[index]) * 16777619);
        }
        BinaryPrimitives.WriteUInt32BigEndian(payload[^sizeof(uint)..], checksum);
    }

    private sealed class LoopbackBearer : IBearer
    {
        private readonly Pipe _incoming = new();
        private readonly Pipe _outgoing = new();

        public PipeReader Reader => _incoming.Reader;
        public PipeWriter Writer => _outgoing.Writer;
        public PipeReader Outgoing => _outgoing.Reader;

        public void Dispose()
        {
            _incoming.Writer.Complete();
            _incoming.Reader.Complete();
            _outgoing.Writer.Complete();
            _outgoing.Reader.Complete();
        }
    }
}
