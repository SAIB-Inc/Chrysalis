using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public class Muxer(IBearer bearer)
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.Now;

    public async Task WriteSegmentAsync(ProtocolType protocol, byte[] payload, CancellationToken cancellationToken)
    {
        uint timestamp = (uint)(DateTime.Now - _startTime).TotalMilliseconds;
        
        MuxSegment segment = new(
            TransmissionTime: timestamp,
            ProtocolId: protocol,
            PayloadLength: (ushort)payload.Length,
            Payload: payload,
            Mode: false
        );

        byte[] encoded = MuxSegmentCodec.Encode(segment);
        await bearer.SendAsync(encoded, cancellationToken);
    }
}
