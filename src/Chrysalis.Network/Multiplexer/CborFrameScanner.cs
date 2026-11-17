using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Chrysalis.Network.Multiplexer;

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

    [InlineArray(64)]
    private struct Frames { private int _element; }

    /// <summary>A single frame's traversal. Dispose before discarding or reusing; never copy a live state.</summary>
    internal struct State : IDisposable
    {
        private Frames _frames;
        private int[]? _spill;
        private int _depth;
        private int _minimumLength;
        private int _headRemaining;
        private int _bodyRemaining;
        private int _major;
        private int _additional;
        private ulong _argument;
        private int _result;

        internal int BytesScanned { readonly get; private set; }
        /// <summary>
        /// Scans only new bytes. Returns the total frame length, zero for NeedMore, or -1 for Invalid;
        /// consumed is the prefix used from this span. No byte from an earlier call is revisited.
        /// </summary>
        internal int Scan(ReadOnlySpan<byte> data, out int consumed)
        {
            consumed = 0;
            if (_result != 0) { return _result; }
            int[]? spill = _spill;
            Span<int> frames = spill is null ? _frames : spill;
            int depth = _depth;
            int minimumLength = _minimumLength;
            int headRemaining = _headRemaining;
            int bodyRemaining = _bodyRemaining;
            int major = _major;
            int additional = _additional;
            ulong argument = _argument;
            int scanned = BytesScanned;
            int offset = 0;
            int result = 0;
            if (depth == 0) { depth = 1; minimumLength = 1; frames[0] = 1; }
            try
            {
                if (bodyRemaining != 0) { goto Body; }
                if (headRemaining != 0) { goto Head; }

            Next:
                if (offset == data.Length) { goto Done; }
                int parent = frames[depth - 1];
                byte initial = data[offset++];
                major = initial >> 5;
                additional = initial & 31;
                if (initial == 0xff)
                {
                    if (parent is >= 0 or MapValue) { goto Invalid; }
                    depth--;
                    goto Finish;
                }
                if ((parent, major, additional) is (ByteString, not 2, _) or (TextString, not 3, _) or ( <= ByteString, _, 31)) { goto Invalid; }
                int itemMinimum = parent switch { MapKey => 2, MapValue or > 0 => 0, _ => 1 };
                if (itemMinimum > Array.MaxLength - minimumLength) { goto Invalid; }
                minimumLength += itemMinimum;
                argument = 0;
                if (additional < 24) { argument = (uint)additional; }
                else if (additional < 28)
                {
                    int bytes = 1 << (additional - 24);
                    if (bytes > Array.MaxLength - minimumLength) { goto Invalid; }
                    minimumLength += bytes;
                    if (data.Length - offset < bytes) { headRemaining = bytes; goto Head; }
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
                else if (additional != 31 || major is < 2 or > 5) { goto Invalid; }

            Value:
                int children = 0;
                if (additional == 31) { children = major switch { 2 => ByteString, 3 => TextString, 4 => IndefiniteArray, _ => MapKey }; }
                else
                {
                    switch (major)
                    {
                        case 2:
                        case 3:
                            if (argument > (ulong)(Array.MaxLength - minimumLength)) { goto Invalid; }
                            bodyRemaining = (int)argument;
                            minimumLength += bodyRemaining;
                            if (bodyRemaining != 0) { goto Body; }
                            break;
                        case 4:
                        case 5:
                            int multiplier = major == 5 ? 2 : 1;
                            if (argument > (ulong)((Array.MaxLength - minimumLength) / multiplier)) { goto Invalid; }
                            children = (int)argument * multiplier;
                            break;
                        case 6:
                            children = 1;
                            break;
                        case 7 when additional == 24 && argument < 32:
                            goto Invalid;
                        default:
                            break;
                    }
                }
                if (children == 0) { goto Finish; }
                int reserve = children > 0 ? children : 1;
                if (reserve > Array.MaxLength - minimumLength) { goto Invalid; }
                minimumLength += reserve;
                if (depth == frames.Length)
                {
                    int[] grown = ArrayPool<int>.Shared.Rent(frames.Length <= int.MaxValue / 2 ? frames.Length * 2 : int.MaxValue);
                    frames.CopyTo(grown);
                    if (spill is not null) { ArrayPool<int>.Shared.Return(spill); }
                    spill = grown;
                    frames = grown;
                }
                frames[depth++] = children;
                goto Next;

            Head:
                while (headRemaining != 0 && offset < data.Length)
                {
                    argument = (argument << 8) | data[offset++];
                    headRemaining--;
                }
                if (headRemaining != 0) { goto Done; }
                goto Value;

            Body:
                int count = Math.Min(bodyRemaining, data.Length - offset);
                offset += count;
                bodyRemaining -= count;
                if (bodyRemaining != 0) { goto Done; }

            Finish:
                while (depth > 0)
                {
                    int remaining = frames[depth - 1];
                    if (remaining < 0)
                    {
                        if (remaining == MapKey) { frames[depth - 1] = MapValue; }
                        else if (remaining == MapValue) { frames[depth - 1] = MapKey; }
                        goto Next;
                    }
                    frames[depth - 1] = remaining - 1;
                    if (remaining > 1) { goto Next; }
                    depth--;
                }
                result = scanned + offset;
                goto Done;

            Invalid:
                result = -1;
            Done:;
            }
            catch
            {
                _spill = spill;
                throw;
            }
            _spill = spill;
            _depth = depth;
            _minimumLength = minimumLength;
            _headRemaining = headRemaining;
            _bodyRemaining = bodyRemaining;
            _major = major;
            _additional = additional;
            _argument = argument;
            _result = result;
            BytesScanned = scanned + offset;
            consumed = offset;
            return result;
        }

        public void Dispose()
        {
            if (_spill is not null) { ArrayPool<int>.Shared.Return(_spill); }
            this = default;
        }
    }

    /// <summary>
    /// Returns a positive frame length, zero for NeedMore, or -1 for Invalid. Lengths/counts must
    /// fit the existing int-sized SAIB reader and Array.MaxLength receive buffer; there is no new cap.
    /// </summary>
    internal static int GetFrameLength(ReadOnlySpan<byte> data)
    {
        State state = new();
        try { return state.Scan(data, out _); }
        finally { state.Dispose(); }
    }
}
