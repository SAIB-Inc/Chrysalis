// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
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

    private enum Kind : byte { List, Map, Constr }

    private readonly record struct Frame(Kind Kind, int Start, long Remaining, ulong Index, bool Definite, bool BreakAfter);

    public static PlutusData DecodePlutusData(ReadOnlyMemory<byte> data)
    {
        int offset = 0;
        return ReadData(data.Span, ref offset);
    }

    // Iterative and depth first: no nesting depth can overflow the stack (the node has no depth limit).
    private static PlutusData ReadData(ReadOnlySpan<byte> span, ref int offset)
    {
        PooledStack<Frame> frames = new(16);
        PooledStack<PlutusData> values = new(16);
        try
        {
            while (true)
            {
                if (frames.Count > 0 && IsComplete(span, ref offset, frames.Top, values.Count))
                {
                    PlutusData built = Close(span, ref offset, frames.Pop(), ref values);
                    if (frames.Count == 0)
                    {
                        return built;
                    }

                    values.Push(built);
                    continue;
                }

                if (Open(span, ref offset, ref frames, values.Count) is { } leaf)
                {
                    if (frames.Count == 0)
                    {
                        return leaf;
                    }

                    values.Push(leaf);
                }
            }
        }
        finally
        {
            values.Dispose();
            frames.Dispose();
        }
    }

    private static bool IsComplete(ReadOnlySpan<byte> span, ref int offset, Frame frame, int valueCount)
    {
        int read = valueCount - frame.Start;
        if (frame.Remaining >= 0)
        {
            return read == frame.Remaining;
        }

        if (frame.Kind == Kind.Map && (read & 1) != 0)
        {
            return false;
        }

        if (At(span, offset) == 0xFF)
        {
            offset++;
            return true;
        }

        return false;
    }

    private static PlutusData? Open(ReadOnlySpan<byte> span, ref int offset, ref PooledStack<Frame> frames, int valueCount)
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
        switch (majorType)
        {
            case 0:
                return new PlutusDataInteger(new BigInteger(ReadHead(span, ref offset, 0)));
            case 1:
                return new PlutusDataInteger(-1 - new BigInteger(ReadHead(span, ref offset, 1)));
            case 2:
                return new PlutusDataByteString(ReadByteString(span, ref offset));
            case 4:
                OpenItems(span, ref offset, ref frames, valueCount, Kind.List, 0, breakAfter: false);
                return null;
            case 5:
                if (At(span, offset) == 0xBF)
                {
                    offset++;
                    frames.Push(new(Kind.Map, valueCount, -1, 0, Definite: false, BreakAfter: false));
                }
                else
                {
                    int count = ReadCount(span, ref offset, 5);
                    frames.Push(new(Kind.Map, valueCount, (long)count * 2, 0, Definite: true, BreakAfter: false));
                }

                return null;
            case 6:
                ulong tag = ReadHead(span, ref offset, 6);
                if (tag is >= 121 and <= 127)
                {
                    OpenItems(span, ref offset, ref frames, valueCount, Kind.Constr, tag - 121, breakAfter: false);
                    return null;
                }

                if (tag is >= 1280 and <= 1400)
                {
                    OpenItems(span, ref offset, ref frames, valueCount, Kind.Constr, tag - 1280 + 7, breakAfter: false);
                    return null;
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
                    OpenItems(span, ref offset, ref frames, valueCount, Kind.Constr, constrIndex, breakAfter: indefiniteOuter);
                    return null;
                }

                throw new InvalidOperationException($"CBOR: unexpected tag {tag} for PlutusData.");
            default:
                throw new InvalidOperationException($"CBOR: unexpected major type {majorType} for PlutusData.");
        }
    }

    private static void OpenItems(ReadOnlySpan<byte> span, ref int offset, ref PooledStack<Frame> frames, int valueCount, Kind kind, ulong index, bool breakAfter)
    {
        if (At(span, offset) == 0x9F)
        {
            offset++;
            frames.Push(new(kind, valueCount, -1, index, Definite: false, BreakAfter: breakAfter));
        }
        else
        {
            int count = ReadCount(span, ref offset, 4);
            frames.Push(new(kind, valueCount, count, index, Definite: true, BreakAfter: breakAfter));
        }
    }

    private static PlutusData Close(ReadOnlySpan<byte> span, ref int offset, Frame frame, ref PooledStack<PlutusData> values)
    {
        ReadOnlySpan<PlutusData> items = values.From(frame.Start);
        PlutusData built = frame.Kind switch
        {
            Kind.List => new PlutusDataList(items.IsEmpty ? [] : ImmutableArray.Create(items), frame.Definite),
            Kind.Map => new PlutusDataMap(Pairs(items)),
            Kind.Constr => new PlutusDataConstr(new BigInteger(frame.Index), items.IsEmpty ? [] : ImmutableArray.Create(items), frame.Definite),
            _ => throw new InvalidOperationException($"CBOR: unknown frame kind {frame.Kind}.")
        };
        values.Truncate(frame.Start);
        if (frame.BreakAfter)
        {
            if (At(span, offset) != 0xFF)
            {
                throw new InvalidOperationException("CBOR: tag 102 expects 2-element array.");
            }

            offset++;
        }

        return built;
    }

    private static ImmutableArray<(PlutusData Key, PlutusData Value)> Pairs(ReadOnlySpan<PlutusData> items)
    {
        if (items.IsEmpty)
        {
            return [];
        }

        (PlutusData, PlutusData)[] pairs = new (PlutusData, PlutusData)[items.Length >> 1];
        for (int i = 0; i < pairs.Length; i++)
        {
            pairs[i] = (items[i * 2], items[(i * 2) + 1]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(pairs);
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
