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
        Channel<ReadOnlySequence<byte>> channel = Channel.CreateBounded<ReadOnlySequence<byte>>(50);
        _protocolChannels.TryAdd(protocol, channel);

        return channel;
    }

    public async Task<MuxSegment> ReadSegmentAsync(CancellationToken cancellationToken)
    {
        ReadResult result = await bearer.Reader.ReadAtLeastAsync(8, cancellationToken);
        ReadOnlySequence<byte> resultBuffer = result.Buffer;
        ReadOnlySequence<byte> headerSlice = resultBuffer.Slice(0, 8);
        MuxSegmentHeader headerSegment = MuxSegmentCodec.DecodeHeader(headerSlice);

        if (resultBuffer.Length >= headerSegment.PayloadLength + 8)
        {
            ReadOnlySequence<byte> payloadSlice = resultBuffer.Slice(8, headerSegment.PayloadLength);
            ReadOnlySequence<byte> payloadSequence = new(payloadSlice.First);
            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(headerSegment.PayloadLength + 8));

            return new(headerSegment, payloadSequence);
        }
        else
        {
            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(8));
            result = await bearer.Reader.ReadAtLeastAsync(headerSegment.PayloadLength, cancellationToken);
            resultBuffer = result.Buffer;

            ReadOnlySequence<byte> payloadSlice = resultBuffer.Slice(0, headerSegment.PayloadLength);
            ReadOnlySequence<byte> payloadSequence = new(payloadSlice.First);
            bearer.Reader.AdvanceTo(resultBuffer.GetPosition(headerSegment.PayloadLength));

            return new(headerSegment, payloadSequence);
        }
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