using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public class Demuxer(IBearer bearer) : IDisposable
{
    private readonly Dictionary<ProtocolType, Channel<ReadOnlySequence<byte>>> _protocolChannels = [];

    public ChannelReader<ReadOnlySequence<byte>> Subscribe(ProtocolType protocol)
    {
        Channel<ReadOnlySequence<byte>> channel = Channel.CreateBounded<ReadOnlySequence<byte>>(ProtocolConstants.MAX_CHANNEL_LOAD);
        _protocolChannels.TryAdd(protocol, channel);

        return channel;
    }

    public async Task<MuxSegment> ReadSegmentAsync(CancellationToken cancellationToken)
    {
        ReadResult result = await bearer.Reader.ReadAtLeastAsync(ProtocolConstants.SEGMENT_HEADER_SIZE, cancellationToken);
        ReadOnlySequence<byte> resultBuffer = result.Buffer;
        ReadOnlySequence<byte> headerSlice = resultBuffer.Slice(0, ProtocolConstants.SEGMENT_HEADER_SIZE);
        MuxSegmentHeader headerSegment = MuxSegmentCodec.DecodeHeader(headerSlice);
        ReadOnlySequence<byte> payloadSequence;

        if (resultBuffer.Length >= headerSegment.PayloadLength + ProtocolConstants.SEGMENT_HEADER_SIZE)
        {
            ReadOnlySequence<byte> payloadSlice = resultBuffer.Slice(ProtocolConstants.SEGMENT_HEADER_SIZE, headerSegment.PayloadLength);

            byte[] payloadCopy = new byte[payloadSlice.Length];
            payloadSlice.CopyTo(payloadCopy);
            payloadSequence = new ReadOnlySequence<byte>(payloadCopy);

            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(headerSegment.PayloadLength + ProtocolConstants.SEGMENT_HEADER_SIZE));
        }
        else
        {
            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(ProtocolConstants.SEGMENT_HEADER_SIZE));
            result = await bearer.Reader.ReadAtLeastAsync(headerSegment.PayloadLength, cancellationToken);
            resultBuffer = result.Buffer;

            ReadOnlySequence<byte> payloadSlice = resultBuffer.Slice(0, headerSegment.PayloadLength);

            byte[] payloadCopy = new byte[payloadSlice.Length];
            payloadSlice.CopyTo(payloadCopy);
            payloadSequence = new ReadOnlySequence<byte>(payloadCopy);

            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(headerSegment.PayloadLength));
        }

        return new(headerSegment, payloadSequence);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            MuxSegment segment = await ReadSegmentAsync(cancellationToken);
            await _protocolChannels[segment.Header.ProtocolId].Writer.WriteAsync(segment.Payload, cancellationToken);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        bearer.Dispose();
    }
}