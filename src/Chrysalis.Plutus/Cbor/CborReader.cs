// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cbor;

/// <summary>
/// Minimal CBOR decoder for PlutusData only.
/// Implements spec Appendix B (sections B.3–B.7) and PlutusCore/Data.hs:209-307.
/// Works over ReadOnlyMemory to avoid copying.
/// </summary>
public static class CborReader
{
    private const int MaxByteStringChunk = 64;

    public static PlutusData DecodePlutusData(ReadOnlyMemory<byte> data)
    {
        int offset = 0;
        return ReadData(data.Span, ref offset);
    }

    private static PlutusData ReadData(ReadOnlySpan<byte> span, ref int offset)
    {
        byte initial = At(span, offset);
        if (initial is 0xC2 or 0xC3)
        {
            offset++;
            ReadOnlyMemory<byte> magnitude = ReadByteString(span, ref offset);
            BigInteger value = new(magnitude.Span, isUnsigned: true, isBigEndian: true);
            return new PlutusDataInteger(initial == 0xC2 ? value : -1 - value);
        }

        int majorType = initial >> 5;
        return majorType switch
        {
            0 => new PlutusDataInteger(new BigInteger(ReadHead(span, ref offset, 0))),
            1 => new PlutusDataInteger(-1 - new BigInteger(ReadHead(span, ref offset, 1))),
            2 => new PlutusDataByteString(ReadByteString(span, ref offset)),
            4 => DecodeList(span, ref offset),
            5 => DecodeMap(span, ref offset),
            6 => DecodeTaggedData(span, ref offset, ReadHead(span, ref offset, 6)),
            _ => throw new InvalidOperationException($"CBOR: unexpected major type {majorType} for PlutusData.")
        };
    }

    private static PlutusDataConstr DecodeTaggedData(ReadOnlySpan<byte> span, ref int offset, ulong tag)
    {
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
            bool indefiniteOuter = At(span, offset) == 0x9F;
            if (indefiniteOuter)
            {
                offset++;
            }
            else if (ReadCount(span, ref offset, 4) != 2)
            {
                throw new InvalidOperationException("CBOR: tag 102 expects 2-element array.");
            }
            ulong constrIndex = ReadHead(span, ref offset, 0);
            (ImmutableArray<PlutusData> fields, bool isDefinite) = ReadDataListWithEncoding(span, ref offset);
            if (indefiniteOuter)
            {
                if (At(span, offset) != 0xFF)
                {
                    throw new InvalidOperationException("CBOR: tag 102 expects 2-element array.");
                }
                offset++;
            }
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
        ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder entries =
            ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>();

        if (At(span, offset) == 0xBF)
        {
            offset++;
            while (At(span, offset) != 0xFF)
            {
                PlutusData key = ReadData(span, ref offset);
                PlutusData value = ReadData(span, ref offset);
                entries.Add((key, value));
            }
            offset++;
        }
        else
        {
            int count = ReadCount(span, ref offset, 5);
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
        ImmutableArray<PlutusData>.Builder items = ImmutableArray.CreateBuilder<PlutusData>();

        if (At(span, offset) == 0x9F)
        {
            offset++;
            while (At(span, offset) != 0xFF)
            {
                items.Add(ReadData(span, ref offset));
            }
            offset++;
            return (items.ToImmutable(), false);
        }
        else
        {
            int count = ReadCount(span, ref offset, 4);
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadData(span, ref offset));
            }
            return (items.ToImmutable(), true);
        }
    }

    // --- Low-level CBOR primitives ---

    private static byte At(ReadOnlySpan<byte> span, int offset) =>
        offset < span.Length ? span[offset] : throw Truncated();

    private static InvalidOperationException Truncated() =>
        new("CBOR: unexpected end of input.");

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> span, ref int offset, int count)
    {
        if (count < 0 || count > span.Length - offset)
        {
            throw Truncated();
        }
        ReadOnlySpan<byte> slice = span.Slice(offset, count);
        offset += count;
        return slice;
    }

    private static ulong ReadHead(ReadOnlySpan<byte> span, ref int offset, int expectedMajor)
    {
        byte initial = At(span, offset);
        int major = initial >> 5;
        if (major != expectedMajor)
        {
            throw new InvalidOperationException($"CBOR: expected major type {expectedMajor}, got {major}.");
        }
        offset++;
        int additionalInfo = initial & 0x1F;

        return additionalInfo switch
        {
            < 24 => (ulong)additionalInfo,
            24 => Take(span, ref offset, 1)[0],
            25 => BinaryPrimitives.ReadUInt16BigEndian(Take(span, ref offset, 2)),
            26 => BinaryPrimitives.ReadUInt32BigEndian(Take(span, ref offset, 4)),
            27 => BinaryPrimitives.ReadUInt64BigEndian(Take(span, ref offset, 8)),
            _ => throw new InvalidOperationException($"CBOR: unsupported additional info {additionalInfo}.")
        };
    }

    private static int ReadCount(ReadOnlySpan<byte> span, ref int offset, int expectedMajor)
    {
        ulong count = ReadHead(span, ref offset, expectedMajor);
        return count > (ulong)(span.Length - offset) ? throw Truncated() : (int)count;
    }

    private static ReadOnlyMemory<byte> ReadByteString(ReadOnlySpan<byte> span, ref int offset)
    {
        if (At(span, offset) == 0x5F)
        {
            offset++;
            ImmutableArray<byte>.Builder result = ImmutableArray.CreateBuilder<byte>();
            while (At(span, offset) != 0xFF)
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
        ulong length = ReadHead(span, ref offset, 2);
        if (length > MaxByteStringChunk)
        {
            throw new InvalidOperationException($"CBOR: definite byte string length {length} exceeds {MaxByteStringChunk} bytes.");
        }
        return Take(span, ref offset, (int)length).ToArray();
    }
}
