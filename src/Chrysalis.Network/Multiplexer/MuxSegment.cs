namespace Chrysalis.Network.Multiplexer;

public record MuxSegment(
    uint TransmissionTime,
    ProtocolType ProtocolId,
    ushort PayloadLength,
    byte[] Payload,
    bool Mode
);