using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cbor;

/// <summary>
/// Minimal CBOR encoder for PlutusData only.
/// Implements spec Appendix B (sections B.3–B.7).
/// Uses ArrayBufferWriter for efficient output.
/// </summary>
internal static class CborWriter
{
    internal static byte[] EncodePlutusData(PlutusData data)
    {
        ArrayBufferWriter<byte> buffer = new();
        WriteData(buffer, data);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteData(ArrayBufferWriter<byte> buffer, PlutusData data)
    {
        switch (data)
        {
            case PlutusDataInteger i:
                WriteInteger(buffer, i.Value);
                break;
            case PlutusDataByteString bs:
                WriteByteString(buffer, bs.Value.Span);
                break;
            case PlutusDataList list:
                WriteUnsigned(buffer, (ulong)list.Values.Length, 4);
                foreach (PlutusData item in list.Values)
                {
                    WriteData(buffer, item);
                }
                break;
            case PlutusDataMap map:
                WriteUnsigned(buffer, (ulong)map.Entries.Length, 5);
                foreach ((PlutusData key, PlutusData value) in map.Entries)
                {
                    WriteData(buffer, key);
                    WriteData(buffer, value);
                }
                break;
            case PlutusDataConstr constr:
                WriteConstrData(buffer, constr);
                break;
            default:
                throw new InvalidOperationException($"CBOR: cannot encode {data.GetType().Name}.");
        }
    }

    private static void WriteConstrData(ArrayBufferWriter<byte> buffer, PlutusDataConstr constr)
    {
        long tag = (long)constr.Tag;

        if (tag is >= 0 and <= 6)
        {
            WriteTag(buffer, (ulong)(121 + tag));
            WriteDefiniteArray(buffer, constr.Fields);
        }
        else if (tag is >= 7 and <= 127)
        {
            WriteTag(buffer, (ulong)(1280 + tag - 7));
            WriteDefiniteArray(buffer, constr.Fields);
        }
        else
        {
            WriteTag(buffer, 102);
            WriteUnsigned(buffer, 2, 4);
            WriteUnsigned(buffer, (ulong)tag, 0);
            WriteDefiniteArray(buffer, constr.Fields);
        }
    }

    private static void WriteDefiniteArray(
        ArrayBufferWriter<byte> buffer,
        System.Collections.Immutable.ImmutableArray<PlutusData> items)
    {
        WriteUnsigned(buffer, (ulong)items.Length, 4);
        foreach (PlutusData item in items)
        {
            WriteData(buffer, item);
        }
    }

    private static void WriteInteger(ArrayBufferWriter<byte> buffer, BigInteger value)
    {
        if (value >= 0)
        {
            if (value <= ulong.MaxValue)
            {
                WriteUnsigned(buffer, (ulong)value, 0);
            }
            else
            {
                WriteTag(buffer, 2);
                byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
                WriteByteString(buffer, bytes);
            }
        }
        else
        {
            BigInteger encoded = -1 - value;
            if (encoded <= ulong.MaxValue)
            {
                WriteUnsigned(buffer, (ulong)encoded, 1);
            }
            else
            {
                WriteTag(buffer, 3);
                byte[] bytes = encoded.ToByteArray(isUnsigned: true, isBigEndian: true);
                WriteByteString(buffer, bytes);
            }
        }
    }

    // --- Low-level CBOR primitives ---

    private static void WriteUnsigned(ArrayBufferWriter<byte> buffer, ulong value, int majorType)
    {
        int mt = majorType << 5;

        if (value < 24)
        {
            WriteSingleByte(buffer, (byte)(mt | (int)value));
        }
        else if (value <= byte.MaxValue)
        {
            Span<byte> span = buffer.GetSpan(2);
            span[0] = (byte)(mt | 24);
            span[1] = (byte)value;
            buffer.Advance(2);
        }
        else if (value <= ushort.MaxValue)
        {
            Span<byte> span = buffer.GetSpan(3);
            span[0] = (byte)(mt | 25);
            BinaryPrimitives.WriteUInt16BigEndian(span[1..], (ushort)value);
            buffer.Advance(3);
        }
        else if (value <= uint.MaxValue)
        {
            Span<byte> span = buffer.GetSpan(5);
            span[0] = (byte)(mt | 26);
            BinaryPrimitives.WriteUInt32BigEndian(span[1..], (uint)value);
            buffer.Advance(5);
        }
        else
        {
            Span<byte> span = buffer.GetSpan(9);
            span[0] = (byte)(mt | 27);
            BinaryPrimitives.WriteUInt64BigEndian(span[1..], value);
            buffer.Advance(9);
        }
    }

    private static void WriteTag(ArrayBufferWriter<byte> buffer, ulong tag) => WriteUnsigned(buffer, tag, 6);

    private static void WriteByteString(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> bytes)
    {
        WriteUnsigned(buffer, (ulong)bytes.Length, 2);
        Span<byte> span = buffer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        buffer.Advance(bytes.Length);
    }

    private static void WriteSingleByte(ArrayBufferWriter<byte> buffer, byte value)
    {
        Span<byte> span = buffer.GetSpan(1);
        span[0] = value;
        buffer.Advance(1);
    }
}
