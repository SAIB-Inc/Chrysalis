using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides minimal and highly optimized methods for encoding and decoding MuxSegments.
/// </summary>
/// <remarks>
/// The codec implements a binary protocol with the following header format:
/// - 4 bytes: Transmission time (uint32, big endian)
/// - 2 bytes: Protocol ID and mode flag (uint16, big endian, high bit = mode)
/// - 2 bytes: Payload length (uint16, big endian)
///
/// Performance is prioritized through aggressive inlining and avoiding struct copies.
/// </remarks>
public static class MuxSegmentCodec
{
    /// <summary>
    /// Encodes a MuxSegment into a binary representation.
    /// </summary>
    /// <param name="segment">The segment to encode.</param>
    /// <returns>A sequence containing the binary representation of the segment.</returns>
    /// <remarks>
    /// The encoded format consists of an 8-byte header followed by the payload:
    /// - Bytes 0-3: Transmission time (uint32, big endian)
    /// - Bytes 4-5: Protocol ID with high bit representing mode (uint16, big endian)
    /// - Bytes 6-7: Payload length (uint16, big endian)
    /// - Bytes 8+: Payload data
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySequence<byte> Encode(in MuxSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        MuxSegmentHeader header = segment.Header;
        byte[] result = new byte[ProtocolConstants.SegmentHeaderSize + header.PayloadLength];
        WriteHeader(result, header.TransmissionTime, header.ProtocolId, header.PayloadLength, header.Mode);
        if (header.PayloadLength > 0 && !segment.Payload.IsEmpty)
        {
            segment.Payload.CopyTo(result.AsSpan(ProtocolConstants.SegmentHeaderSize));
        }
        return new ReadOnlySequence<byte>(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteHeader(Span<byte> span, uint transmissionTime, ProtocolType protocolId, ushort payloadLength, bool responder)
    {
        BinaryPrimitives.WriteUInt32BigEndian(span, transmissionTime);
        ushort protocolIdAndMode = (ushort)protocolId;
        if (responder) { protocolIdAndMode |= 0x8000; }
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), protocolIdAndMode);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), payloadLength);
    }

    /// <summary>
    /// Decodes a binary sequence into a MuxSegmentHeader.
    /// </summary>
    /// <param name="headerSequence">The binary sequence containing the header data (must be at least 8 bytes).</param>
    /// <returns>The decoded MuxSegmentHeader.</returns>
    /// <remarks>
    /// This method extracts fields from the binary header:
    /// - Transmission time: 4-byte uint32 in big endian
    /// - Protocol ID: 15 lower bits of the 2-byte field in big endian
    /// - Mode flag: High bit (bit 15) of the protocol ID field
    /// - Payload length: 2-byte uint16 in big endian
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MuxSegmentHeader DecodeHeader(in ReadOnlySequence<byte> headerSequence)
    {
        if (headerSequence.IsSingleSegment)
        {
            return DecodeHeaderSpan(headerSequence.FirstSpan);
        }

        Span<byte> buf = stackalloc byte[ProtocolConstants.SegmentHeaderSize];
        headerSequence.CopyTo(buf);
        return DecodeHeaderSpan(buf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MuxSegmentHeader DecodeHeaderSpan(ReadOnlySpan<byte> span)
    {
        ReadHeader(span, out uint transmissionTime, out ProtocolType protocolId, out ushort payloadLength, out bool mode);
        return new(transmissionTime, protocolId, payloadLength, mode);
    }

    internal static void ReadHeader(ReadOnlySpan<byte> span, out uint transmissionTime, out ProtocolType protocolId, out ushort payloadLength, out bool mode)
    {
        transmissionTime = BinaryPrimitives.ReadUInt32BigEndian(span);
        ushort protocolIdAndMode = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
        mode = (protocolIdAndMode & 0x8000) != 0;
        protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF);
        payloadLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));
    }
}
