using System.Buffers;
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
        ReadOnlySequence<byte> headerData = await bearer.ReceiveExactAsync(8, cancellationToken);
        MuxSegmentHeader headerSegment = MuxSegmentCodec.DecodeHeader(headerData);
        ReadOnlySequence<byte> payload = await bearer.ReceiveExactAsync(headerSegment.PayloadLength, cancellationToken);
        return new(headerSegment, payload);
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