using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cbor;

/// <summary>
/// Minimal CBOR decoder for PlutusData only.
/// Implements spec Appendix B (sections B.3–B.7).
/// Works over ReadOnlyMemory to avoid copying.
/// </summary>
public static class CborReader
{
    public static PlutusData DecodePlutusData(ReadOnlyMemory<byte> data)
    {
        int offset = 0;
        return ReadData(data.Span, ref offset);
    }

    private static PlutusData ReadData(ReadOnlySpan<byte> span, ref int offset)
    {
        int majorType = span[offset] >> 5;

        if (majorType == 6)
        {
            ulong tag = ReadUnsigned(span, ref offset);
            return DecodeTaggedData(span, ref offset, tag);
        }

        return majorType switch
        {
            0 => new PlutusDataInteger(new BigInteger(ReadUnsigned(span, ref offset))),
            1 => new PlutusDataInteger(-1 - new BigInteger(ReadUnsigned(span, ref offset))),
            2 => new PlutusDataByteString(ReadByteString(span, ref offset)),
            4 => DecodeList(span, ref offset),
            5 => DecodeMap(span, ref offset),
            _ => throw new InvalidOperationException($"CBOR: unexpected major type {majorType} for PlutusData.")
        };
    }

    private static PlutusData DecodeTaggedData(ReadOnlySpan<byte> span, ref int offset, ulong tag)
    {
        if (tag == 2)
        {
            ReadOnlyMemory<byte> value = ReadByteString(span, ref offset);
            return new PlutusDataInteger(new BigInteger(value.Span, isUnsigned: true, isBigEndian: true));
        }

        if (tag == 3)
        {
            ReadOnlyMemory<byte> value = ReadByteString(span, ref offset);
            return new PlutusDataInteger(-1 - new BigInteger(value.Span, isUnsigned: true, isBigEndian: true));
        }

        if (tag is >= 121 and <= 127)
        {
            (ImmutableArray<PlutusData> fields, bool isDefinite) = ReadDataListWithEncoding(span, ref offset);
            return new PlutusDataConstr(new BigInteger(tag - 121), fields, isDefinite);
        }

        if (tag is >= 1280 and <= 1400)
        {
            (ImmutableArray<PlutusData> fields, bool isDefinite) = ReadDataListWithEncoding(span, ref offset);
            return new PlutusDataConstr(new BigInteger(tag - 1280 + 7), fields, isDefinite);
        }

        if (tag == 102)
        {
            int count = ReadArrayHeader(span, ref offset);
            if (count != 2)
            {
                throw new InvalidOperationException($"CBOR: tag 102 expects 2-element array, got {count}.");
            }
            ulong constrIndex = ReadUnsigned(span, ref offset);
            (ImmutableArray<PlutusData> fields, bool isDefinite) = ReadDataListWithEncoding(span, ref offset);
            return new PlutusDataConstr(new BigInteger(constrIndex), fields, isDefinite);
        }

        throw new InvalidOperationException($"CBOR: unexpected tag {tag} for PlutusData.");
    }

    private static PlutusDataList DecodeList(ReadOnlySpan<byte> span, ref int offset)
    {
        (ImmutableArray<PlutusData> items, bool isDefinite) = ReadDataListWithEncoding(span, ref offset);
        return new PlutusDataList(items, isDefinite);
    }

    private static PlutusDataMap DecodeMap(ReadOnlySpan<byte> span, ref int offset)
    {
        int additionalInfo = span[offset] & 0x1F;

        ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder entries =
            ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>();

        if (additionalInfo == 31)
        {
            offset++;
            while (span[offset] != 0xFF)
            {
                PlutusData key = ReadData(span, ref offset);
                PlutusData value = ReadData(span, ref offset);
                entries.Add((key, value));
            }
            offset++;
        }
        else
        {
            int count = ReadArrayHeader(span, ref offset);
            for (int i = 0; i < count; i++)
            {
                PlutusData key = ReadData(span, ref offset);
                PlutusData value = ReadData(span, ref offset);
                entries.Add((key, value));
            }
        }

        return new PlutusDataMap(entries.ToImmutable());
    }

    private static (ImmutableArray<PlutusData> Items, bool IsDefinite) ReadDataListWithEncoding(ReadOnlySpan<byte> span, ref int offset)
    {
        int additionalInfo = span[offset] & 0x1F;

        ImmutableArray<PlutusData>.Builder items = ImmutableArray.CreateBuilder<PlutusData>();

        if (additionalInfo == 31)
        {
            offset++;
            while (span[offset] != 0xFF)
            {
                items.Add(ReadData(span, ref offset));
            }
            offset++;
            return (items.ToImmutable(), false);
        }
        else
        {
            int count = ReadArrayHeader(span, ref offset);
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadData(span, ref offset));
            }
            return (items.ToImmutable(), true);
        }
    }

    // --- Low-level CBOR primitives ---

    private static ulong ReadUnsigned(ReadOnlySpan<byte> span, ref int offset)
    {
        byte initial = span[offset++];
        int additionalInfo = initial & 0x1F;

        return additionalInfo switch
        {
            < 24 => (ulong)additionalInfo,
            24 => span[offset++],
            25 => ReadU16(span, ref offset),
            26 => ReadU32(span, ref offset),
            27 => ReadU64(span, ref offset),
            _ => throw new InvalidOperationException($"CBOR: unsupported additional info {additionalInfo}.")
        };
    }

    private static int ReadArrayHeader(ReadOnlySpan<byte> span, ref int offset) => (int)ReadUnsigned(span, ref offset);

    private static ReadOnlyMemory<byte> ReadByteString(ReadOnlySpan<byte> span, ref int offset)
    {
        int additionalInfo = span[offset] & 0x1F;

        if (additionalInfo == 31)
        {
            offset++;
            ImmutableArray<byte>.Builder result = ImmutableArray.CreateBuilder<byte>();
            while (span[offset] != 0xFF)
            {
                ReadOnlyMemory<byte> chunk = ReadDefiniteByteString(span, ref offset);
                result.AddRange(chunk.Span);
            }
            offset++;
            return result.ToArray();
        }

        return ReadDefiniteByteString(span, ref offset);
    }

    private static ReadOnlyMemory<byte> ReadDefiniteByteString(ReadOnlySpan<byte> span, ref int offset)
    {
        int length = (int)ReadUnsigned(span, ref offset);
        byte[] result = span.Slice(offset, length).ToArray();
        offset += length;
        return result;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> span, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
        offset += 2;
        return value;
    }

    private static uint ReadU32(ReadOnlySpan<byte> span, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(span[offset..]);
        offset += 4;
        return value;
    }

    private static ulong ReadU64(ReadOnlySpan<byte> span, ref int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(span[offset..]);
        offset += 8;
        return value;
    }
}
