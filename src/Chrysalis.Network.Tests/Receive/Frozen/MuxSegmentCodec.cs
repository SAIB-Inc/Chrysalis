// Frozen DecodeHeader and DecodeHeaderSpan from fc21a49; no receive dependency follows Q7.
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.Tests.Receive.Frozen;

internal static class MuxSegmentCodec
{
    private const int HeaderSize = 8;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MuxSegmentHeader DecodeHeader(in ReadOnlySequence<byte> headerSequence)
    {
        if (headerSequence.IsSingleSegment)
        {
            return DecodeHeaderSpan(headerSequence.FirstSpan);
        }

        Span<byte> buf = stackalloc byte[HeaderSize];
        headerSequence.CopyTo(buf);
        return DecodeHeaderSpan(buf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MuxSegmentHeader DecodeHeaderSpan(ReadOnlySpan<byte> span)
    {
        uint transmissionTime = BinaryPrimitives.ReadUInt32BigEndian(span);
        ushort protocolIdAndMode = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
        bool mode = (protocolIdAndMode & 0x8000) != 0;
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF);
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));

        return new(
            transmissionTime,
            protocolId,
            payloadLength,
            mode
        );
    }
}
