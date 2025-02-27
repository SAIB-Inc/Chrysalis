using Chrysalis.Network.Core;
using SArray = System.Array;
namespace Chrysalis.Network.Multiplexer;

// Convert from record to class with mutable properties
public class MuxSegment
{
    public uint TransmissionTime { get; set; }
    public ProtocolType ProtocolId { get; set; }
    public ushort PayloadLength { get; set; }
    public byte[] Payload { get; set; }
    public bool Mode { get; set; }

    // Required parameterless constructor for pooling
    public MuxSegment()
    {
        Payload = [];
    }

    // Convenience constructor that mimics the record
    public MuxSegment(uint transmissionTime, ProtocolType protocolId, ushort payloadLength, byte[] payload, bool mode)
    {
        TransmissionTime = transmissionTime;
        ProtocolId = protocolId;
        PayloadLength = payloadLength;
        Payload = payload;
        Mode = mode;
    }

    // This helps reset the object when returning to pool
    public void Reset()
    {
        TransmissionTime = 0;
        ProtocolId = ProtocolType.Handshake; // Default value
        PayloadLength = 0;
        Payload = [];
        Mode = false;
    }
}