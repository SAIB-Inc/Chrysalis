using Chrysalis.Network.Core;
using System;
using System.IO;

namespace Chrysalis.Network.Multiplexer;

public static class MuxSegmentCodec
{
    public static byte[] Encode(MuxSegment segment)
    {
        using var memoryStream = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memoryStream);

        EncodeHeaderInternal(segment, binaryWriter); // Reuse header encoding logic

        // 4. Payload (byte array)
        binaryWriter.Write(segment.Payload);

        return memoryStream.ToArray();
    }

    // New method to Encode Header only
    public static byte[] EncodeHeader(MuxSegment segment)
    {
        using var memoryStream = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memoryStream);

        EncodeHeaderInternal(segment, binaryWriter); // Reuse header encoding logic

        return memoryStream.ToArray();
    }


    // Private method to encapsulate header encoding logic (reused by Encode and EncodeHeader)
    private static void EncodeHeaderInternal(MuxSegment segment, BinaryWriter binaryWriter)
    {
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
    }


    public static MuxSegment Decode(byte[] bytes)
    {
        using var memoryStream = new MemoryStream(bytes);
        using var binaryReader = new BinaryReader(memoryStream);

        return DecodeInternal(binaryReader, true); // Decode full segment (including payload)
    }


     // New method to Decode Header only
    public static MuxSegment DecodeHeader(byte[] bytes)
    {
         using var memoryStream = new MemoryStream(bytes);
        using var binaryReader = new BinaryReader(memoryStream);

        return DecodeInternal(binaryReader, false); // Decode only header (payload is empty)
    }


    // Private method to encapsulate decoding logic (reused by Decode and DecodeHeader)
    private static MuxSegment DecodeInternal(BinaryReader binaryReader, bool decodePayload)
    {
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

        byte[] payload = Array.Empty<byte>(); // Default to empty payload

        // 4. Payload (byte array) - only decode if decodePayload is true
        if (decodePayload)
        {
             payload = binaryReader.ReadBytes(payloadLength); // Read exactly PayloadLength bytes
        }


        return new MuxSegment(
            TransmissionTime: transmissionTime,
            ProtocolId: protocolId,
            PayloadLength: payloadLength,
            Payload: payload,
            Mode: mode
        );
    }
}