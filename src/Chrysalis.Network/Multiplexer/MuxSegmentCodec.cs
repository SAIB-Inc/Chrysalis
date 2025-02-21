using System.IO;
using System.Net;

namespace Chrysalis.Network.Multiplexer;

public static class MuxSegmentCodec
{
    public static byte[] Encode(MuxSegment segment)
    {
        using var memoryStream = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memoryStream);

        // 1. Transmission Time (uint32, big-endian)
        byte[] transmissionTimeBytes = BitConverter.GetBytes(segment.TransmissionTime);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(transmissionTimeBytes); // Convert to big-endian if necessary
        }
        binaryWriter.Write(transmissionTimeBytes);

        // 2. Mini Protocol ID (ushort with Mode bit, big-endian)
        ushort protocolIdValue = (ushort)segment.ProtocolId;
        if (segment.Mode)
        {
            protocolIdValue |= (ushort)0x8000; // Set the most significant bit for Mode = 1
        }
        byte[] protocolIdBytes = BitConverter.GetBytes(protocolIdValue);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(protocolIdBytes); // Convert to big-endian
        }
        binaryWriter.Write(protocolIdBytes);

        // 3. Payload Length (ushort, big-endian)
        byte[] payloadLengthBytes = BitConverter.GetBytes(segment.PayloadLength);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(payloadLengthBytes); // Convert to big-endian
        }
        binaryWriter.Write(payloadLengthBytes);

        // 4. Payload (byte array)
        binaryWriter.Write(segment.Payload);

        return memoryStream.ToArray();
    }

    public static MuxSegment Decode(byte[] bytes)
    {
        using var memoryStream = new MemoryStream(bytes);
        using var binaryReader = new BinaryReader(memoryStream);

        // 1. Transmission Time (uint32, big-endian)
        byte[] transmissionTimeBytes = binaryReader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(transmissionTimeBytes);
        }
        uint transmissionTime = BitConverter.ToUInt32(transmissionTimeBytes, 0);

        // 2. Mini Protocol ID (ushort with Mode bit, big-endian)
        byte[] protocolIdBytes = binaryReader.ReadBytes(2);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(protocolIdBytes);
        }
        ushort protocolIdAndMode = BitConverter.ToUInt16(protocolIdBytes, 0);
        bool mode = (protocolIdAndMode & 0x8000) != 0; // Check the most significant bit
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF); // Mask out the mode bit

        // 3. Payload Length (ushort, big-endian)
        byte[] payloadLengthBytes = binaryReader.ReadBytes(2);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(payloadLengthBytes);
        }
        ushort payloadLength = BitConverter.ToUInt16(payloadLengthBytes, 0);

        // 4. Payload (byte array)
        byte[] payload = binaryReader.ReadBytes(payloadLength); // Read exactly PayloadLength bytes

        return new MuxSegment(
            TransmissionTime: transmissionTime,
            ProtocolId: protocolId,
            PayloadLength: payloadLength,
            Payload: payload,
            Mode: mode
        );
    }
}