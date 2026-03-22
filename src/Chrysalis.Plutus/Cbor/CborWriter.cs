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
public static class CborWriter
{
    public static byte[] EncodePlutusData(PlutusData data)
    {
        ArgumentNullException.ThrowIfNull(data);
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
                WritePlutusByteString(buffer, bs.Value.Span);
                break;
            case PlutusDataList list:
                WriteIndefiniteArray(buffer, list.Values);
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
            WriteIndefiniteArray(buffer, constr.Fields);
        }
        else if (tag is >= 7 and <= 127)
        {
            WriteTag(buffer, (ulong)(1280 + tag - 7));
            WriteIndefiniteArray(buffer, constr.Fields);
        }
        else
        {
            WriteTag(buffer, 102);
            WriteUnsigned(buffer, 2, 4);
            WriteUnsigned(buffer, (ulong)tag, 0);
            WriteIndefiniteArray(buffer, constr.Fields);
        }
    }

    private static void WriteIndefiniteArray(
        ArrayBufferWriter<byte> buffer,
        System.Collections.Immutable.ImmutableArray<PlutusData> items)
    {
        WriteSingleByte(buffer, 0x9F);
        foreach (PlutusData item in items)
        {
            WriteData(buffer, item);
        }
        WriteSingleByte(buffer, 0xFF);
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
                WritePlutusByteString(buffer, bytes);
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
                WritePlutusByteString(buffer, bytes);
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

    private static void WritePlutusByteString(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> bytes)
    {
        const int ChunkSize = 64;

        if (bytes.Length <= ChunkSize)
        {
            WriteDefiniteByteString(buffer, bytes);
        }
        else
        {
            WriteSingleByte(buffer, 0x5F); // indefinite-length bytestring
            int offset = 0;
            while (offset < bytes.Length)
            {
                int remaining = bytes.Length - offset;
                int chunkLen = remaining < ChunkSize ? remaining : ChunkSize;
                WriteDefiniteByteString(buffer, bytes.Slice(offset, chunkLen));
                offset += chunkLen;
            }
            WriteSingleByte(buffer, 0xFF); // break
        }
    }

    private static void WriteDefiniteByteString(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> bytes)
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
