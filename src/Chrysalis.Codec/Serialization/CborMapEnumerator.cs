using System.ComponentModel;
using Chrysalis.Codec.Types;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

/// <summary>Compiler hook enabling collection expressions such as <c>[]</c> for <see cref="CborMap{TKey, TValue}"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CborMapCollectionBuilder
{
    public static CborMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<KeyValuePair<TKey, TValue>> entries)
        where TKey : notnull => CborMap<TKey, TValue>.FromSpan(entries);
}

/// <summary>Shared wire cursor for <see cref="CborMap{TKey, TValue}"/> reading, probing, and enumeration.</summary>
internal struct CborMapCursor(ReadOnlyMemory<byte> data, int offset)
{
    private readonly ReadOnlyMemory<byte> _data = data;

    internal int Offset { get; private set; } = offset;
    internal readonly bool HasNext => Offset < _data.Length && _data.Span[Offset] != 0xFF;

    internal static CborMapCursor Open(ReadOnlyMemory<byte> data)
    {
        CborReader reader = new(data.Span);
        reader.ReadBeginMap();
        _ = reader.ReadSize();
        return new(data, data.Length - reader.Buffer.Length);
    }

    internal T ReadItem<T>(out int start)
    {
        start = Offset;
        T result = DecodeFrom<T>(_data, start, out int consumed);
        Offset = start + consumed;
        return result;
    }

    internal TKey ReadKeyWithHash<TKey>(IEqualityComparer<TKey> comparer, out int keyStart, out int keyHash) where TKey : notnull
    {
        TKey key = ReadItem<TKey>(out keyStart);
        try { keyHash = HashCode.Combine(comparer.GetHashCode(key)); }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, _data[keyStart..], "CborMap.entry", CborError.Expected(typeof(TKey)), keyStart);
        }
        return key;
    }

    internal int SkipValue()
    {
        int start = Offset;
        try
        {
            CborReader reader = new(_data.Span[start..]);
            reader.SkipDataItem();
            Offset = _data.Length - reader.Buffer.Length;
            return start;
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, _data[start..], "CborMap.entry", "map value", start);
        }
    }

    internal static T DecodeFrom<T>(ReadOnlyMemory<byte> data, int start) => DecodeFrom<T>(data, start, out _);

    internal static T DecodeFrom<T>(ReadOnlyMemory<byte> data, int start, out int consumed)
    {
        if ((uint)start < (uint)data.Length && data.Span[start] == 0xF6)
        {
            throw CborError.Decode(data, start, "CborMap.entry", CborError.Expected(typeof(T)), "null");
        }
        T? result;
        try { result = CborCodecCache<T>.GetReaderOrThrow(data[start..])(data[start..], out consumed); }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data[start..], "CborMap.entry", CborError.Expected(typeof(T)), start);
        }
        return result ?? throw CborError.Decode(data, start, "CborMap.entry", CborError.Expected(typeof(T)), "null");
    }

    internal static bool KeysEqual<TKey>(IEqualityComparer<TKey> comparer, TKey x, TKey y, ReadOnlyMemory<byte> data, int keyStart)
        where TKey : notnull
    {
        try { return comparer.Equals(x, y); }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data[keyStart..], "CborMap.entry", CborError.Expected(typeof(TKey)), keyStart);
        }
    }
}

internal struct CborMapEntryWalker<TKey, TValue>(ReadOnlyMemory<byte> raw, object? state) where TKey : notnull
{
    private readonly ReadOnlyMemory<byte> _raw = raw;
    private readonly KeyValuePair<TKey, TValue>[]? _entries = state as KeyValuePair<TKey, TValue>[];
    private readonly int[]? _dupIndex = state as int[];
    private CborMapCursor _cursor;
    private int _position;

    internal KeyValuePair<TKey, TValue> Current { get; private set; }

    internal bool MoveNext()
    {
        if (_entries is not null)
        {
            if (_position >= _entries.Length) { return false; }
            Current = _entries[_position++];
            return true;
        }
        if (_dupIndex is not null)
        {
            if (_position >= _dupIndex.Length) { return false; }
            int pos = _position;
            _position += 2;
            Current = new(CborMapCursor.DecodeFrom<TKey>(_raw, _dupIndex[pos]), CborMapCursor.DecodeFrom<TValue>(_raw, _dupIndex[pos + 1]));
            return true;
        }
        if (_raw.IsEmpty) { return false; }
        if (_cursor.Offset == 0) { _cursor = CborMapCursor.Open(_raw); }
        if (!_cursor.HasNext) { return false; }
        Current = new(_cursor.ReadItem<TKey>(out _), _cursor.ReadItem<TValue>(out _));
        return true;
    }

    internal void Reset() { _cursor = default; _position = 0; }
}

/// <summary>Ref-struct enumerator over deduplicated entries of a <see cref="CborMap{TKey, TValue}"/>.</summary>
public ref struct CborMapEnumerator<TKey, TValue> where TKey : notnull
{
    private CborMapEntryWalker<TKey, TValue> _walker;

    internal CborMapEnumerator(ReadOnlyMemory<byte> raw, object? state) => _walker = new(raw, state);

    public readonly KeyValuePair<TKey, TValue> Current => _walker.Current;
    public bool MoveNext() => _walker.MoveNext();
}
