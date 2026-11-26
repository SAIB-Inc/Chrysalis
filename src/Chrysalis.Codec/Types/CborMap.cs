using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Serialization.Utils;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types;

/// <summary>
/// Lazy wire-backed CBOR map preserving raw bytes verbatim with deduplicated ledger semantics
/// (first-seen key and position paired with last-seen value).
/// </summary>
/// <remarks>
/// <see cref="TryGetValue"/>, <see cref="ContainsKey"/>, and the indexer perform a linear O(N) scan;
/// call <see cref="ToDictionary"/> once before probing repeatedly in a loop.
/// Built-in <c>byte[]</c> keys use byte-content equality and full-span hashing.
/// Custom keys in a lazy map must compare by value across re-decoding; reference identity is insufficient.
/// Fresh entries reject <see cref="ICborType"/> items backed by CBOR wire null (<c>0xF6</c>) on <see cref="Create"/> and <see cref="Write"/>; custom non-<see cref="ICborType"/> writers must not emit <c>0xF6</c>.
/// </remarks>
[CborSerializable]
[CollectionBuilder(typeof(CborMapCollectionBuilder), nameof(CborMapCollectionBuilder.Create))]
public readonly partial record struct CborMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, ICborType
    where TKey : notnull
{
    private const byte FlagIndefinite = 1;
    private const int StackKeyLimit = 32;

    // 4 disjoint states in one 8 B slot: default (Raw empty, null); fresh (Raw empty, KVP[]);
    // decoded unique (!Raw empty, null); decoded duplicates (!Raw empty, int[] [firstKeyOff, lastValOff, ...]).
    private readonly object? _state;
    public ReadOnlyMemory<byte> Raw { get; }
    public int Count { get; }
    private readonly byte _flags;

    public bool IsIndefinite => (_flags & FlagIndefinite) != 0;
    public bool IsEmpty => Count == 0;
    int ICborType.ConstrIndex => -1;

    private CborMap(ReadOnlyMemory<byte> raw, object? state, int count, byte flags)
    {
        Raw = raw;
        _state = state;
        Count = count;
        _flags = flags;
    }

    public bool Equals(CborMap<TKey, TValue> other) =>
        !Raw.IsEmpty ? Raw.Equals(other.Raw) : other.Raw.IsEmpty && ReferenceEquals(_state, other._state) && _flags == other._flags;

    public override int GetHashCode() => !Raw.IsEmpty ? Raw.GetHashCode() : HashCode.Combine(_state, _flags);

    internal static CborMap<TKey, TValue> CreateCore(IEnumerable<KeyValuePair<TKey, TValue>> entries, bool isIndefinite)
    {
        ArgumentNullException.ThrowIfNull(entries);
        byte flags = isIndefinite ? FlagIndefinite : (byte)0;
        IEqualityComparer<TKey> comparer = LedgerKeyComparers.For<TKey>();

        if (entries is Dictionary<TKey, TValue> dict && ReferenceEquals(dict.Comparer, comparer))
        {
            if (dict.Count == 0) { return new(default, Array.Empty<KeyValuePair<TKey, TValue>>(), 0, flags); }
            KeyValuePair<TKey, TValue>[] copy = new KeyValuePair<TKey, TValue>[dict.Count];
            int idx = 0;
            foreach (KeyValuePair<TKey, TValue> entry in dict) { ValidateEntry(entry); copy[idx++] = entry; }
            return new(default, copy, copy.Length, flags);
        }

        List<KeyValuePair<TKey, TValue>> list = [];
        Dictionary<TKey, int>? seen = null;
        foreach (KeyValuePair<TKey, TValue> entry in entries)
        {
            ValidateEntry(entry);
            if (seen is null && list.Count < 16)
            {
                int found = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (comparer.Equals(list[i].Key, entry.Key)) { found = i; break; }
                }
                if (found >= 0) { list[found] = new(list[found].Key, entry.Value); }
                else { list.Add(entry); }
            }
            else
            {
                if (seen is null)
                {
                    seen = [with(list.Count + 1, comparer)];
                    for (int i = 0; i < list.Count; i++) { seen[list[i].Key] = i; }
                }
                if (seen.TryGetValue(entry.Key, out int existing)) { list[existing] = new(list[existing].Key, entry.Value); }
                else { seen[entry.Key] = list.Count; list.Add(entry); }
            }
        }
        KeyValuePair<TKey, TValue>[] snapshot = list.Count == 0 ? [] : [.. list];
        return new(default, snapshot, snapshot.Length, flags);
    }

    internal static CborMap<TKey, TValue> FromSpan(ReadOnlySpan<KeyValuePair<TKey, TValue>> entries) =>
        entries.IsEmpty ? new(default, Array.Empty<KeyValuePair<TKey, TValue>>(), 0, 0) : CreateCore(entries.ToArray(), false);

    private static bool IsWireNull<T>(T item) => item is ICborType cbor && !cbor.Raw.IsEmpty && cbor.Raw.Span[0] == 0xF6;

    private static void ValidateEntry(KeyValuePair<TKey, TValue> entry)
    {
        if (entry.Key is null) { throw new ArgumentNullException(nameof(entry), "Map key cannot be null."); }
        if (IsWireNull(entry.Key)) { throw CborError.Validation(-1, "CborMap", "non-null map key", "null"); }
        if (entry.Value is null || IsWireNull(entry.Value)) { throw CborError.Validation(-1, "CborMap", "non-null map value", "null"); }
    }

    public CborMapEnumerator<TKey, TValue> GetEnumerator() => new(Raw, _state);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
        new BoxedEnumerator(Raw, _state);

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (TryFind(key, decodeValue: true, out TValue? found)) { value = found!; return true; }
        value = default;
        return false;
    }

    public bool ContainsKey(TKey key) => TryFind(key, decodeValue: false, out _);

    private bool TryFind(TKey key, bool decodeValue, out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        IEqualityComparer<TKey> comparer = LedgerKeyComparers.For<TKey>();
        if (Raw.IsEmpty)
        {
            if (_state is KeyValuePair<TKey, TValue>[] entries)
            {
                foreach (KeyValuePair<TKey, TValue> entry in entries)
                {
                    if (comparer.Equals(entry.Key, key)) { value = entry.Value; return true; }
                }
            }
            value = default;
            return false;
        }
        if (_state is int[] dupIndex)
        {
            for (int i = 0; i < dupIndex.Length; i += 2)
            {
                int keyStart = dupIndex[i];
                if (CborMapCursor.KeysEqual(comparer, CborMapCursor.DecodeFrom<TKey>(Raw, keyStart), key, Raw, keyStart))
                {
                    value = decodeValue ? CborMapCursor.DecodeFrom<TValue>(Raw, dupIndex[i + 1]) : default;
                    return true;
                }
            }
            value = default;
            return false;
        }
        CborMapCursor cursor = CborMapCursor.Open(Raw);
        while (cursor.HasNext)
        {
            TKey candidate = cursor.ReadItem<TKey>(out int keyStart);
            if (CborMapCursor.KeysEqual(comparer, candidate, key, Raw, keyStart))
            {
                value = decodeValue ? cursor.ReadItem<TValue>(out _) : default;
                return true;
            }
            _ = cursor.SkipValue();
        }
        value = default;
        return false;
    }

    public TValue this[TKey key] =>
        TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException($"Key not found in CborMap: {key}");

    public Dictionary<TKey, TValue> ToDictionary()
    {
        Dictionary<TKey, TValue> result = [with(Math.Clamp(Count, 0, 256), LedgerKeyComparers.For<TKey>())];
        foreach (KeyValuePair<TKey, TValue> entry in this) { result[entry.Key] = entry.Value; }
        return result;
    }

    internal static CborMap<TKey, TValue> ReadCore(
        ReadOnlyMemory<byte> data, out int bytesConsumed, out int probeSteps, out int keyComparisons)
    {
        probeSteps = 0;
        keyComparisons = 0;
        if (data.IsEmpty || (data.Span[0] >> 5) != 5) { throw CborError.Decode(data, 0, "CborMap", "map"); }

        int[]? rented = null;
        try
        {
            CborReader header = new(data.Span);
            header.ReadBeginMap();
            int declaredSize = header.ReadSize();
            bool indefinite = declaredSize == -1;
            int bodyStart = data.Length - header.Buffer.Length;
            if (declaredSize == 0) { bytesConsumed = bodyStart; return new(data[..bytesConsumed], null, 0, 0); }

            IEqualityComparer<TKey> comparer = LedgerKeyComparers.For<TKey>();
            CborMapCursor cursor = new(data, bodyStart);
            int capacity = StackKeyLimit;
            Span<int> offsets = stackalloc int[StackKeyLimit * 2];
            Span<int> hashes = stackalloc int[StackKeyLimit];
            Span<int> buckets = stackalloc int[StackKeyLimit * 2];
            buckets.Clear();

            int distinctCount = 0;
            bool hasDuplicates = false;
            for (int wireIndex = 0; indefinite ? cursor.HasNext : wireIndex < declaredSize; wireIndex++)
            {
                if (!indefinite && (cursor.Offset >= data.Length || data.Span[cursor.Offset] == 0xFF)) { throw CborError.Decode(data, cursor.Offset, "CborMap", "map key"); }
                TKey key = cursor.ReadKeyWithHash(comparer, out int keyStart, out int keyHash);
                if (cursor.Offset >= data.Length || data.Span[cursor.Offset] == 0xFF) { throw CborError.Decode(data, cursor.Offset, "CborMap", "map value"); }
                int valStart = cursor.SkipValue();

                int mask = buckets.Length - 1;
                int b = keyHash & mask;
                int foundSlot = -1;
                while (buckets[b] != 0)
                {
                    probeSteps++;
                    int slot = buckets[b] - 1;
                    if (hashes[slot] == keyHash)
                    {
                        keyComparisons++;
                        TKey existing = CborMapCursor.DecodeFrom<TKey>(data, offsets[slot * 2]);
                        if (CborMapCursor.KeysEqual(comparer, existing, key, data, keyStart)) { foundSlot = slot; break; }
                    }
                    b = (b + 1) & mask;
                }

                if (foundSlot >= 0) { hasDuplicates = true; offsets[(foundSlot * 2) + 1] = valStart; continue; }

                if (distinctCount == capacity)
                {
                    int newCap = checked(capacity * 2);
                    int[] nextRented = ArrayPool<int>.Shared.Rent(checked(newCap * 5));
                    Span<int> nextOffsets = nextRented.AsSpan(0, newCap * 2);
                    Span<int> nextHashes = nextRented.AsSpan(newCap * 2, newCap);
                    Span<int> nextBuckets = nextRented.AsSpan(newCap * 3, newCap * 2);
                    nextBuckets.Clear();
                    offsets[..(distinctCount * 2)].CopyTo(nextOffsets);
                    hashes[..distinctCount].CopyTo(nextHashes);

                    // Rehash existing slots using saved hashes only: zero key comparisons and zero key re-decodes.
                    int nextMask = nextBuckets.Length - 1;
                    for (int s = 0; s < distinctCount; s++)
                    {
                        int rb = nextHashes[s] & nextMask;
                        while (nextBuckets[rb] != 0) { probeSteps++; rb = (rb + 1) & nextMask; }
                        nextBuckets[rb] = s + 1;
                    }
                    if (rented is not null) { ArrayPool<int>.Shared.Return(rented); }
                    rented = nextRented;
                    capacity = newCap;
                    offsets = nextOffsets;
                    hashes = nextHashes;
                    buckets = nextBuckets;

                    b = keyHash & nextMask;
                    while (buckets[b] != 0) { probeSteps++; b = (b + 1) & nextMask; }
                }

                int newSlot = distinctCount++;
                offsets[newSlot * 2] = keyStart;
                offsets[(newSlot * 2) + 1] = valStart;
                hashes[newSlot] = keyHash;
                buckets[b] = newSlot + 1;
            }

            if (indefinite && cursor.Offset >= data.Length) { throw CborError.Decode(data, data.Length, "CborMap", "end of indefinite map"); }
            bytesConsumed = indefinite ? cursor.Offset + 1 : cursor.Offset;
            int[]? dupIndex = hasDuplicates ? offsets[..(distinctCount * 2)].ToArray() : null;
            return new(data[..bytesConsumed], dupIndex, distinctCount, indefinite ? FlagIndefinite : (byte)0);
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error)) { throw CborError.Wrap(error, data, "CborMap", "map"); }
        finally { if (rented is not null) { ArrayPool<int>.Shared.Return(rented); } }
    }

    internal static void WriteCore(IBufferWriter<byte> output, CborMap<TKey, TValue> value)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!value.Raw.IsEmpty) { output.Write(value.Raw.Span); return; }
        if (value._state is not KeyValuePair<TKey, TValue>[] entries) { throw CborError.Validation(-1, "CborMap", "decoded map or map built from entries", "default value"); }
        CborWriter writer = new(output);
        int size = value.IsIndefinite ? -1 : entries.Length;
        writer.WriteBeginMap(size);
        foreach (KeyValuePair<TKey, TValue> entry in entries)
        {
            ValidateEntry(entry);
            GenericSerializationUtil.Write(output, entry.Key);
            GenericSerializationUtil.Write(output, entry.Value);
        }
        writer.WriteEndMap(size);
    }

    private sealed class BoxedEnumerator(ReadOnlyMemory<byte> raw, object? state) : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private CborMapEntryWalker<TKey, TValue> _walker = new(raw, state);

        public KeyValuePair<TKey, TValue> Current => _walker.Current;
        object IEnumerator.Current => Current;

        public bool MoveNext() => _walker.MoveNext();
        public void Reset() => _walker.Reset();
        public void Dispose() { }
    }
}

internal static class CborMapExtensions
{
    internal static Dictionary<string, TValue> ToHexKeyDictionary<TValue>(this CborMap<ReadOnlyMemory<byte>, TValue> map)
    {
        Dictionary<string, TValue> result = [with(Math.Clamp(map.Count, 0, 256))];
        foreach ((ReadOnlyMemory<byte> key, TValue value) in map) { result[Convert.ToHexString(key.Span)] = value; }
        return result;
    }
}
