using Chrysalis.Network.Core;
using SysArray = System.Array;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides methods for encoding and decoding mux segments using functional programming concepts.
/// </summary>
public static class MuxSegmentCodec
{
    /// <summary>
    /// Encodes a full mux segment (header and payload) into a byte array.
    /// </summary>
    /// <param name="segment">The mux segment to encode.</param>
    /// <returns>A byte array representing the encoded mux segment.</returns>
    public static byte[] Encode(MuxSegment segment)
    {
        using var memoryStream = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memoryStream);

        // Encode the header.
        EncodeHeaderInternal(segment, binaryWriter);

        // Write the payload.
        binaryWriter.Write(segment.Payload);

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Encodes only the header of a mux segment into a byte array.
    /// </summary>
    /// <param name="segment">The mux segment whose header to encode.</param>
    /// <returns>A byte array representing the encoded header.</returns>
    public static byte[] EncodeHeader(MuxSegment segment)
    {
        using var memoryStream = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memoryStream);

        // Encode the header.
        EncodeHeaderInternal(segment, binaryWriter);

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Wraps the encoding of a full mux segment in a Try monad to capture any exceptions.
    /// </summary>
    /// <param name="segment">The mux segment to encode.</param>
    /// <returns>
    /// A Try monad yielding the encoded byte array, or capturing an exception if one occurs.
    /// </returns>
    public static Try<byte[]> TryEncode(MuxSegment segment) =>
        Try(() => Encode(segment));

    /// <summary>
    /// Wraps the encoding of a mux segment header in a Try monad.
    /// </summary>
    /// <param name="segment">The mux segment whose header to encode.</param>
    /// <returns>
    /// A Try monad yielding the encoded header byte array, or capturing an exception if one occurs.
    /// </returns>
    public static Try<byte[]> TryEncodeHeader(MuxSegment segment) =>
        Try(() => EncodeHeader(segment));

    /// <summary>
    /// Decodes a full mux segment (header and payload) from a byte array.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>A mux segment containing the decoded header and payload.</returns>
    public static MuxSegment Decode(byte[] bytes)
    {
        using var memoryStream = new MemoryStream(bytes);
        using var binaryReader = new BinaryReader(memoryStream);
        return DecodeInternal(binaryReader, decodePayload: true);
    }

    /// <summary>
    /// Decodes only the header of a mux segment from a byte array.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>A mux segment containing the decoded header with an empty payload.</returns>
    public static MuxSegment DecodeHeader(byte[] bytes)
    {
        using var memoryStream = new MemoryStream(bytes);
        using var binaryReader = new BinaryReader(memoryStream);
        return DecodeInternal(binaryReader, decodePayload: false);
    }

    /// <summary>
    /// Wraps the decoding of a full mux segment in a Try monad to capture any exceptions.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>
    /// A Try monad yielding the mux segment, or capturing an exception if one occurs.
    /// </returns>
    public static Try<MuxSegment> TryDecode(byte[] bytes) =>
        Try(() => Decode(bytes));

    /// <summary>
    /// Wraps the decoding of a mux segment header in a Try monad.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>
    /// A Try monad yielding the mux segment header, or capturing an exception if one occurs.
    /// </returns>
    public static Try<MuxSegment> TryDecodeHeader(byte[] bytes) =>
        Try(() => DecodeHeader(bytes));

    /// <summary>
    /// Attempts to decode a full mux segment from a byte array.
    /// Returns an Option containing the segment if successful, or None if decoding fails.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>An Option containing the decoded mux segment, or None on failure.</returns>
    public static Option<MuxSegment> DecodeOption(byte[] bytes) =>
        TryDecode(bytes).ToOption();

    /// <summary>
    /// Encodes the header portion of a mux segment.
    /// </summary>
    /// <param name="segment">The mux segment to encode.</param>
    /// <param name="binaryWriter">The binary writer used to write to the memory stream.</param>
    private static void EncodeHeaderInternal(MuxSegment segment, BinaryWriter binaryWriter)
    {
        // 1. Transmission Time (uint32, big-endian)
        byte[] transmissionTimeBytes = BitConverter.GetBytes(segment.TransmissionTime);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(transmissionTimeBytes);
        }
        binaryWriter.Write(transmissionTimeBytes);

        // 2. Mini Protocol ID (ushort with Mode bit, big-endian)
        ushort protocolIdValue = (ushort)segment.ProtocolId;
        if (segment.Mode)
        {
            protocolIdValue |= 0x8000; // Set the most significant bit for Mode = 1
        }
        byte[] protocolIdBytes = BitConverter.GetBytes(protocolIdValue);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(protocolIdBytes);
        }
        binaryWriter.Write(protocolIdBytes);

        // 3. Payload Length (ushort, big-endian)
        byte[] payloadLengthBytes = BitConverter.GetBytes(segment.PayloadLength);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(payloadLengthBytes);
        }
        binaryWriter.Write(payloadLengthBytes);
    }

    /// <summary>
    /// Decodes a mux segment from a binary reader.
    /// </summary>
    /// <param name="binaryReader">The binary reader to read from.</param>
    /// <param name="decodePayload">If true, also decodes the payload; otherwise, the payload remains empty.</param>
    /// <returns>The decoded mux segment.</returns>
    private static MuxSegment DecodeInternal(BinaryReader binaryReader, bool decodePayload)
    {
        // 1. Transmission Time (uint32, big-endian)
        byte[] transmissionTimeBytes = binaryReader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(transmissionTimeBytes);
        }
        uint transmissionTime = BitConverter.ToUInt32(transmissionTimeBytes, 0);

        // 2. Mini Protocol ID (ushort with Mode bit, big-endian)
        byte[] protocolIdBytes = binaryReader.ReadBytes(2);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(protocolIdBytes);
        }
        ushort protocolIdAndMode = BitConverter.ToUInt16(protocolIdBytes, 0);
        bool mode = (protocolIdAndMode & 0x8000) != 0; // Check the most significant bit
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF); // Mask out the mode bit

        // 3. Payload Length (ushort, big-endian)
        byte[] payloadLengthBytes = binaryReader.ReadBytes(2);
        if (BitConverter.IsLittleEndian)
        {
            SysArray.Reverse(payloadLengthBytes);
        }
        ushort payloadLength = BitConverter.ToUInt16(payloadLengthBytes, 0);

        byte[] payload = SysArray.Empty<byte>(); // Use SysArray alias for System.Array

        // 4. Payload (only decode if requested)
        if (decodePayload)
        {
            payload = binaryReader.ReadBytes(payloadLength);
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
