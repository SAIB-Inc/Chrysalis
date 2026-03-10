using System.Collections;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.V2.Serialization;

/// <summary>
/// Zero-allocation ref struct enumerator that iterates CBOR list items directly from raw bytes.
/// Handles definite/indefinite arrays and optional tag-258 prefix.
/// Uses CborListReaderCache{T} for source-generated fast-path deserialization.
/// </summary>
public ref struct CborListEnumerator<T>
{
    private readonly ReadOnlyMemory<byte> _data;
    private ReadOnlySpan<byte> _remaining;

    public T Current { get; private set; }

    public CborListEnumerator(ReadOnlyMemory<byte> data)
    {
        _data = data;
        CborReader reader = new(data.Span);

        // Skip optional tag 258
        if (reader.Buffer.Length > 0)
        {
            _ = reader.TryReadSemanticTag(out _);
        }

        reader.ReadBeginArray();
        _ = reader.ReadSize();
        _remaining = reader.Buffer;
        Current = default!;
    }

    public bool MoveNext()
    {
        if (_remaining.Length == 0 || _remaining[0] == 0xFF)
        {
            return false;
        }

        int offset = _data.Length - _remaining.Length;
        ReadOnlyMemory<byte> itemData = _data[offset..];
        int consumed;

        ReadWithConsumedHandler<T>? cachedReader = CborListReaderCache<T>.Reader;
        if (cachedReader != null)
        {
            Current = cachedReader(itemData, out consumed);
        }
        else
        {
            Current = GenericSerializationUtil.ReadAnyWithConsumed<T>(itemData, out consumed)!;
        }

        _remaining = _remaining[consumed..];
        return true;
    }
}

/// <summary>
/// Enumerable wrapper that enables both zero-alloc foreach (via duck-typed GetEnumerator)
/// and IEnumerable{T} for LINQ compatibility.
/// </summary>
public readonly record struct CborListEnumerable<T>(ReadOnlyMemory<byte> Data) : IEnumerable<T>
{
    /// <summary>
    /// Returns a zero-allocation ref struct enumerator for foreach.
    /// </summary>
    public CborListEnumerator<T> GetEnumerator() => new(Data);

    /// <summary>
    /// IEnumerable{T} implementation for LINQ/interface consumers.
    /// </summary>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new BoxedEnumerator(Data);

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    private sealed class BoxedEnumerator(ReadOnlyMemory<byte> data) : IEnumerator<T>
    {
        private readonly ReadOnlyMemory<byte> _data = data;
        private int _offset;
        private bool _initialized;

        public T Current { get; private set; } = default!;
        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            if (!_initialized)
            {
                CborReader reader = new(_data.Span);
                if (reader.Buffer.Length > 0)
                {
                    _ = reader.TryReadSemanticTag(out _);
                }
                reader.ReadBeginArray();
                _ = reader.ReadSize();
                _offset = _data.Length - reader.Buffer.Length;
                _initialized = true;
            }

            if (_offset >= _data.Length || _data.Span[_offset] == 0xFF)
            {
                return false;
            }

            ReadOnlyMemory<byte> itemData = _data[_offset..];
            int consumed;

            ReadWithConsumedHandler<T>? cachedReader = CborListReaderCache<T>.Reader;
            if (cachedReader != null)
            {
                Current = cachedReader(itemData, out consumed);
            }
            else
            {
                Current = GenericSerializationUtil.ReadAnyWithConsumed<T>(itemData, out consumed)!;
            }

            _offset += consumed;
            return true;
        }

        public void Reset() => _initialized = false;
        public void Dispose() { }
    }
}
