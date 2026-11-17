// Frozen from fc21a49; namespace/import adaptations only. Do not change receive behavior.
using System.Buffers;
using System.Buffers.Binary;

namespace Chrysalis.Network.Tests.Receive.Frozen;

/// <summary>
/// Finds the first CBOR item without decoding it or throwing on incomplete/malformed input.
/// Byte/text strings are skipped by length; their contents remain the semantic decoder's job.
/// </summary>
internal static class CborFrameScanner
{
    private const int IndefiniteArray = -1;
    private const int MapKey = -2;
    private const int MapValue = -3;
    private const int ByteString = -4;
    private const int TextString = -5;

    /// <summary>
    /// Returns a positive frame length, zero for NeedMore, or -1 for Invalid. Lengths/counts must
    /// fit the existing int-sized SAIB reader and Array.MaxLength receive buffer; there is no new cap.
    /// The common path uses only stack storage. Unusually deep containers grow a pooled stack,
    /// rather than recursing or imposing a new nesting limit on otherwise valid chain data.
    /// </summary>
    internal static int GetFrameLength(ReadOnlySpan<byte> data)
    {
        Span<int> frames = stackalloc int[64];
        frames[0] = 1; // Synthetic container with one root item.
        int depth = 1;
        int offset = 0;
        // Minimum TOTAL encoded length of this frame, including not-yet-read siblings and all
        // mandatory parent breaks. It only grows as headers reveal more bytes/children. A root
        // item initially reserves one byte; definite parents reserve every child's first byte.
        int minimumLength = 1;
        int[]? rented = null;
        try
        {
            while (true)
            {
                if (offset == data.Length) { return 0; }
                int parent = frames[depth - 1];
                byte initial = data[offset++];
                int major = initial >> 5;
                int additional = initial & 31;
                int children = 0;

                if (initial == 0xff)
                {
                    // Break is legal only at the boundary of an indefinite container, and never
                    // between a map key and value. Closing a container completes its parent item.
                    if (parent is >= 0 or MapValue) { return -1; }
                    depth--;
                }
                else
                {
                    // Indefinite strings contain definite chunks of the SAME string type only.
                    if ((parent, major, additional) is (ByteString, not 2, _) or (TextString, not 3, _) or ( <= ByteString, _, 31)) { return -1; }

                    // Indefinite parents have no declared child count. Each arriving chunk/item
                    // therefore adds its first byte; a new map key also reserves its value. A
                    // map value's first byte was already reserved when its key arrived.
                    int itemMinimum = parent switch { MapKey => 2, MapValue or > 0 => 0, _ => 1 };
                    if (itemMinimum > Array.MaxLength - minimumLength) { return -1; }
                    minimumLength += itemMinimum;

                    ulong argument;
                    if (additional < 24)
                    {
                        argument = (uint)additional;
                    }
                    else if (additional < 28)
                    {
                        int bytes = 1 << (additional - 24);
                        // The initial byte already requires these header bytes, even if they
                        // have not arrived. Include them before deciding NeedMore.
                        if (bytes > Array.MaxLength - minimumLength) { return -1; }
                        minimumLength += bytes;
                        if (data.Length - offset < bytes) { return 0; }
                        ReadOnlySpan<byte> value = data.Slice(offset, bytes);
                        argument = bytes switch
                        {
                            1 => value[0],
                            2 => BinaryPrimitives.ReadUInt16BigEndian(value),
                            4 => BinaryPrimitives.ReadUInt32BigEndian(value),
                            _ => BinaryPrimitives.ReadUInt64BigEndian(value)
                        };
                        offset += bytes;
                    }
                    else if (additional == 31 && major is >= 2 and <= 5)
                    {
                        argument = 0;
                        children = major switch { 2 => ByteString, 3 => TextString, 4 => IndefiniteArray, _ => MapKey };
                    }
                    else
                    {
                        return -1;
                    }

                    if (additional != 31)
                    {
                        switch (major)
                        {
                            case 2:
                            case 3:
                                if (argument > (ulong)(Array.MaxLength - minimumLength)) { return -1; }
                                minimumLength += (int)argument;
                                if (argument > (ulong)(data.Length - offset)) { return 0; }
                                offset += (int)argument;
                                break;
                            case 4:
                            case 5:
                                // Each child needs at least one byte. A map has two children per
                                // pair; test BEFORE multiplying/narrowing the unsigned argument.
                                int multiplier = major == 5 ? 2 : 1;
                                if (argument > (ulong)((Array.MaxLength - minimumLength) / multiplier)) { return -1; }
                                children = (int)argument * multiplier;
                                break;
                            case 6:
                                children = 1;
                                break;
                            case 7 when additional == 24 && argument < 32:
                                return -1; // Reserved/non-well-formed two-byte simple values.
                            default:
                                break;
                        }
                    }

                    if (children != 0)
                    {
                        // Definite containers/tags reserve their children; an indefinite
                        // container reserves its required closing break. Ancestor reservations
                        // remain in minimumLength, so individually fitting items cannot hide an
                        // impossible aggregate frame.
                        int childMinimum = children > 0 ? children : 1;
                        if (childMinimum > Array.MaxLength - minimumLength) { return -1; }
                        minimumLength += childMinimum;
                        if (depth == frames.Length)
                        {
                            int[] grown = ArrayPool<int>.Shared.Rent(frames.Length <= int.MaxValue / 2 ? frames.Length * 2 : int.MaxValue);
                            frames.CopyTo(grown);
                            if (rented is not null) { ArrayPool<int>.Shared.Return(rented); }
                            rented = grown;
                            frames = grown;
                        }
                        frames[depth++] = children;
                        continue;
                    }
                }

                // Finish this item, and any definite ancestors whose last child it was.
                while (depth > 0)
                {
                    int remaining = frames[depth - 1];
                    if (remaining < 0)
                    {
                        if (remaining == MapKey) { frames[depth - 1] = MapValue; }
                        else if (remaining == MapValue) { frames[depth - 1] = MapKey; }
                        break;
                    }
                    frames[depth - 1] = remaining - 1;
                    if (remaining > 1) { break; }
                    depth--;
                }
                if (depth == 0) { return offset; }
            }
        }
        finally
        {
            if (rented is not null) { ArrayPool<int>.Shared.Return(rented); }
        }
    }
}
