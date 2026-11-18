using System.Buffers.Binary;
using Chrysalis.Codec.Serialization;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types;

internal static class PrimitiveWire
{
    internal static ICborPrimitive Read(ReadOnlyMemory<byte> data, out int consumed)
    {
        byte first = data.IsEmpty ? (byte)0xFF : data.Span[0];
        CborReader reader = new(data.Span);
        switch (first >> 5)
        {
            case 0:
                ulong unsigned = reader.ReadUInt64();
                return unsigned switch
                {
                    <= int.MaxValue => CborInt.Read(data, out consumed),
                    <= long.MaxValue => CborLong.Read(data, out consumed),
                    _ => CborULong.Read(data, out consumed)
                };
            case 1:
                if (first == 0x3B && data.Length >= 9 && BinaryPrimitives.ReadUInt64BigEndian(data.Span[1..]) > long.MaxValue)
                {
                    throw CborError.Decode(data, 0, nameof(ICborPrimitive), "integer representable as Int64");
                }
                return reader.ReadInt64() >= int.MinValue ? CborInt.Read(data, out consumed) : CborLong.Read(data, out consumed);
            case 2: return CborBytes.Read(data, out consumed);
            case 3: return CborString.Read(data, out consumed);
            case 4: return CborArray.Read(data, out consumed);
            case 5: return CborMap.Read(data, out consumed);
            default: break;
        }
        return first switch
        {
            0xF4 or 0xF5 => CborBool.Read(data, out consumed),
            0xFA => CborFloat.Read(data, out consumed),
            0xFB => CborDouble.Read(data, out consumed),
            _ => throw CborError.Decode(data, 0, nameof(ICborPrimitive), "registered primitive wire kind")
        };
    }
}
