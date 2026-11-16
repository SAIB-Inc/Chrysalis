using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class MuxerRecordParsingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QueuedUnalignedRecordsDecodeAcrossPipeSegments(bool splitLengthField)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        Pipe bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        // Fill the first actual pipe segment exactly. The next record starts
        // two bytes before its end (split length), or five (split payload).
        Memory<byte> segment = muxer.Writer.GetMemory(4096);
        int firstRecordLength = segment.Length - (splitLengthField ? 2 : 5);
        Assert.InRange(firstRecordLength, 3, ushort.MaxValue);
        byte[][] payloads = [Payload(firstRecordLength - 3, 0), Payload(19, 1),
            Payload(1, 2), Payload(1027, 3), Payload(0, 4), Payload(4099, 5)];
        byte[] records = EncodeRecords(payloads);
        records.AsSpan(0, segment.Length).CopyTo(segment.Span);
        muxer.Writer.Advance(segment.Length);
        muxer.Writer.Write(records.AsSpan(segment.Length));
        _ = await muxer.Writer.FlushAsync(deadline.Token);
        Task run = muxer.RunAsync(deadline.Token);
        Task read = AssertFramesAsync(bearer.Reader, payloads, deadline.Token);
        await AssertTrafficAsync(run, read, deadline);
        await bearer.Writer.CompleteAsync();
        await bearer.Reader.CompleteAsync();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public async Task PartialRecordWaitsForRemainingBytes(int prefixLength, bool completeRecordFirst)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        Pipe bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        byte[][] payloads = [Payload(37, 0), Payload(11, 1), Payload(250, 2)];
        byte[] records = EncodeRecords(payloads);
        // Also exercise retaining a partial tail after consuming a complete
        // record, rather than only leaving the whole read buffer unconsumed.
        int visibleLength = prefixLength + (completeRecordFirst ? payloads[0].Length + 3 : 0);
        // RunAsync synchronously sees the flushed prefix on its initial read,
        // so a parser that assumes a complete record fails before tail arrival.
        _ = await muxer.Writer.WriteAsync(records.AsMemory(0, visibleLength), deadline.Token);
        Task run = muxer.RunAsync(deadline.Token);
        try
        {
            Assert.False(run.IsCompleted, "A partial record must wait for more bytes, not terminate the muxer");
            _ = await muxer.Writer.WriteAsync(records.AsMemory(visibleLength), deadline.Token);
            await AssertTrafficAsync(run, AssertFramesAsync(bearer.Reader, payloads, deadline.Token), deadline);
        }
        finally
        {
            await deadline.CancelAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await bearer.Writer.CompleteAsync();
            await bearer.Reader.CompleteAsync();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task CompletedInputEndsOrReportsTruncationWithoutSpinning(int prefixLength)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        Pipe bearer = new();
        using SemaphoreSlim bearerWriteLock = new(1, 1);
        using Muxer muxer = new(bearer.Writer, ProtocolMode.Initiator, bearerWriteLock);
        byte[] record = EncodeRecords([Payload(37, 0)]);
        _ = await muxer.Writer.WriteAsync(record.AsMemory(0, prefixLength), deadline.Token);
        await muxer.Writer.CompleteAsync();
        Task run = muxer.RunAsync(deadline.Token);
        try
        {
            if (prefixLength == 0)
            {
                await run.WaitAsync(deadline.Token);
            }
            else
            {
                _ = await Assert.ThrowsAsync<EndOfStreamException>(() => run.WaitAsync(deadline.Token));
            }
        }
        finally
        {
            await bearer.Writer.CompleteAsync();
            await bearer.Reader.CompleteAsync();
        }
    }

    private static async Task AssertTrafficAsync(Task run, Task read, CancellationTokenSource deadline)
    {
        try
        {
            Task completed = await Task.WhenAny(run, read).WaitAsync(deadline.Token);
            await completed;
            Assert.Same(read, completed);
            await read;
        }
        finally
        {
            await deadline.CancelAsync();
            try { await Task.WhenAll(run, read).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task AssertFramesAsync(PipeReader reader, byte[][] payloads, CancellationToken token)
    {
        int index = 0;
        while (index < payloads.Length)
        {
            ReadResult result = await reader.ReadAsync(token);
            ReadOnlySequence<byte> remaining = result.Buffer;
            while (remaining.Length >= ProtocolConstants.SegmentHeaderSize)
            {
                MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(remaining.Slice(0, ProtocolConstants.SegmentHeaderSize));
                long size = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                if (remaining.Length < size) { break; }
                Assert.InRange(index, 0, payloads.Length - 1);
                Assert.Equal((ProtocolType)(index + 2), header.ProtocolId);
                Assert.False(header.Mode);
                Assert.Equal(payloads[index], remaining.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength).ToArray());
                index++;
                remaining = remaining.Slice(size);
            }
            reader.AdvanceTo(remaining.Start, remaining.End);
            Assert.False(result.IsCompleted && index < payloads.Length);
        }
    }

    private static byte[] Payload(int length, int seed)
    {
        byte[] payload = new byte[length];
        for (int index = 0; index < length; index++) { payload[index] = (byte)((seed * 31) + index); }
        return payload;
    }

    private static byte[] EncodeRecords(byte[][] payloads)
    {
        byte[] records = new byte[payloads.Sum(payload => payload.Length + 3)];
        int offset = 0;
        for (int index = 0; index < payloads.Length; index++)
        {
            records[offset] = (byte)(index + 2);
            BinaryPrimitives.WriteUInt16BigEndian(records.AsSpan(offset + 1), (ushort)payloads[index].Length);
            payloads[index].CopyTo(records.AsSpan(offset + 3));
            offset += payloads[index].Length + 3;
        }
        return records;
    }
}
