// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
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
    // An open container: Index counts the items written; Break closes an indefinite array.
    private record struct Cursor(PlutusData Container, int Index, int Count, bool Break);

    /// <summary>
    /// Encodes as the node's serialiseData: non-empty lists and constr fields indefinite, empty ones definite, maps definite.
    /// </summary>
    public static byte[] EncodePlutusData(PlutusData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArrayBufferWriter<byte> buffer = new();
        WriteData(buffer, data, nodeForm: true);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Encodes PlutusData to CBOR using definite-length Constr arrays and keeping each list's IsDefinite flag.
    /// </summary>
    public static byte[] EncodePlutusDataDefinite(PlutusData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArrayBufferWriter<byte> buffer = new();
        WriteData(buffer, data, nodeForm: false);
        return buffer.WrittenSpan.ToArray();
    }

    // Iterative and depth first: no nesting depth can overflow the stack (the node has no depth limit).
    private static void WriteData(ArrayBufferWriter<byte> buffer, PlutusData data, bool nodeForm)
    {
        PooledStack<Cursor> open = new(16);
        try
        {
            PlutusData? next = data;
            while (next is not null)
            {
                switch (next)
                {
                    case PlutusDataInteger i:
                        WriteInteger(buffer, i.Value, chunked: true);
                        break;
                    case PlutusDataByteString bs:
                        WritePlutusByteString(buffer, bs.Value.Span);
                        break;
                    case PlutusDataList list:
                        Open(buffer, ref open, list, list.Values.Length, nodeForm ? list.Values.IsEmpty : list.IsDefinite);
                        break;
                    case PlutusDataMap map:
                        WriteUnsigned(buffer, (ulong)map.Entries.Length, 5);
                        if (!map.Entries.IsEmpty)
                        {
                            open.Push(new(map, 0, map.Entries.Length * 2, Break: false));
                        }
                        break;
                    case PlutusDataConstr constr:
                        WriteConstrTag(buffer, constr.Tag);
                        Open(buffer, ref open, constr, constr.Fields.Length, !nodeForm || constr.Fields.IsEmpty);
                        break;
                    default:
                        throw new InvalidOperationException($"CBOR: cannot encode {next.GetType().Name}.");
                }

                next = Next(buffer, ref open);
            }
        }
        finally
        {
            open.Dispose();
        }
    }

    // serialise-0.2.6.1 Class.hs:174-184 (defaultEncodeList): [] is definite 80, non-empty is indefinite.
    private static void Open(ArrayBufferWriter<byte> buffer, ref PooledStack<Cursor> open, PlutusData container, int count, bool definite)
    {
        if (definite)
        {
            WriteUnsigned(buffer, (ulong)count, 4);
            if (count > 0)
            {
                open.Push(new(container, 0, count, Break: false));
            }
        }
        else
        {
            WriteSingleByte(buffer, 0x9F);
            open.Push(new(container, 0, count, Break: true));
        }
    }

    private static PlutusData? Next(ArrayBufferWriter<byte> buffer, ref PooledStack<Cursor> open)
    {
        while (open.Count > 0)
        {
            ref Cursor top = ref open.Top;
            if (top.Index < top.Count)
            {
                PlutusData item = DataItems.At(top.Container, top.Index++);
                return item ?? throw new InvalidOperationException("CBOR: cannot encode a null item.");
            }

            if (top.Break)
            {
                WriteSingleByte(buffer, 0xFF);
            }

            _ = open.Pop();
        }

        return null;
    }

    // PlutusCore/Data.hs:149-160; cborg Write.hs:150-177,678-689
    private static void WriteConstrTag(ArrayBufferWriter<byte> buffer, BigInteger tag)
    {
        if (tag >= 0 && tag <= 6)
        {
            WriteTag(buffer, 121UL + (ulong)tag);
        }
        else if (tag >= 7 && tag <= 127)
        {
            WriteTag(buffer, 1280UL + (ulong)tag - 7UL);
        }
        else
        {
            WriteTag(buffer, 102);
            WriteUnsigned(buffer, 2, 4);
            WriteInteger(buffer, tag, chunked: false);
        }
    }

    // Data integers chunk magnitudes over 64 B; a tag-102 index uses cborg's encodeInteger (one piece).
    private static void WriteInteger(ArrayBufferWriter<byte> buffer, BigInteger value, bool chunked = true)
    {
        bool negative = value < 0;
        BigInteger magnitude = negative ? -1 - value : value;
        if (magnitude <= ulong.MaxValue)
        {
            WriteUnsigned(buffer, (ulong)magnitude, negative ? 1 : 0);
            return;
        }

        WriteTag(buffer, negative ? 3UL : 2UL);
        byte[] bytes = magnitude.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (chunked)
        {
            WritePlutusByteString(buffer, bytes);
        }
        else
        {
            WriteDefiniteByteString(buffer, bytes);
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
