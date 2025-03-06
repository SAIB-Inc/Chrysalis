using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides minimal and highly optimized methods for encoding and decoding MuxSegments.
/// </summary>
public static class MuxSegmentCodec
{
    // Header size constants
    private const ushort TransmissionTimeSize = 4;
    private const ushort ProtocolIdSize = 2;
    private const ushort PayloadLengthSize = 2;
    public const ushort HeaderSize = TransmissionTimeSize + ProtocolIdSize + PayloadLengthSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySequence<byte> Encode(in MuxSegment segment)
    {
        // Calculate total size needed
        int totalSize = HeaderSize + segment.Header.PayloadLength;

        // Create a result buffer
        byte[] result = new byte[totalSize];

        // Write header
        Span<byte> headerSpan = result.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(headerSpan[..4], segment.Header.TransmissionTime);

        ushort protocolIdValue = (ushort)segment.Header.ProtocolId;
        if (segment.Header.Mode)
        {
            protocolIdValue |= 0x8000;
        }

        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(4, 2), protocolIdValue);
        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(6, 2), segment.Header.PayloadLength);

        // Write payload after header
        if (segment.Header.PayloadLength > 0 && !segment.Payload.IsEmpty)
        {
            Span<byte> payloadSpan = result.AsSpan(HeaderSize, segment.Header.PayloadLength);
            segment.Payload.CopyTo(payloadSpan);
        }

        return new(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MuxSegmentHeader DecodeHeader(in ReadOnlySequence<byte> headerSequence)
    {
        uint transmissionTime = BinaryPrimitives.ReadUInt32BigEndian(headerSequence.Slice(0, 4).FirstSpan);
        ushort protocolIdAndMode = BinaryPrimitives.ReadUInt16BigEndian(headerSequence.Slice(4, 2).FirstSpan);
        bool mode = (protocolIdAndMode & 0x8000) != 0;
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF);
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(headerSequence.Slice(6, 2).FirstSpan);

        return new(
            transmissionTime,
            protocolId,
            payloadLength,
            mode
        );
    }
}