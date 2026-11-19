using System.Buffers;
using System.Collections;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types;

/// <summary>A CBOR array, optionally indefinite and optionally wrapped in set tag 258.</summary>
[CborSerializable]
public readonly partial record struct CborList<T> : ICborList<T>, IEnumerable<T>
{
    private const ulong SetTag = 258;
    private readonly T[]? _items;

    /// <summary>The exact input bytes for a decoded list; empty for a fresh list or the default value.</summary>
    public ReadOnlyMemory<byte> Raw { get; }
    public bool IsIndefinite { get; }
    public bool HasSetTag { get; }
    public int Count { get; }
    int ICborType.ConstrIndex => -1;

    private CborList(ReadOnlyMemory<byte> raw, T[]? items, int count, bool isIndefinite, bool hasSetTag)
    {
        Raw = raw;
        _items = items;
        Count = count;
        IsIndefinite = isIndefinite;
        HasSetTag = hasSetTag;
    }

    /// <summary>Builds a list from a snapshot of the supplied items, in their original order.</summary>
    internal static CborList<T> FromItems(IEnumerable<T> items, bool isIndefinite, bool hasSetTag)
    {
        T[] values = [.. items];
        return new(default, values, values.Length, isIndefinite, hasSetTag);
    }

    public CborListEnumerator<T> GetEnumerator() => Raw.IsEmpty ? new(_items.AsSpan()) : new(Raw);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => Raw.IsEmpty
        ? ((IEnumerable<T>)(_items ?? [])).GetEnumerator()
        : CborListEnumerable<T>.EnumerateRaw(Raw);

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    internal static CborList<T> ReadCore(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            return ReadArray(data, out bytesConsumed);
        }
        catch (CborException error)
        {
            throw CborError.Wrap(error, data, "CborList", "array or set tag 258");
        }
    }

    private static CborList<T> ReadArray(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        bool tagged = !data.IsEmpty && (data.Span[0] >> 5) == 6;
        if (tagged && (!reader.TryReadSemanticTag(out ulong tag) || tag != SetTag))
        {
            throw CborError.Decode(data, 0, "CborList", "array or set tag 258");
        }

        if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 4)
        {
            throw CborError.Decode(data, data.Length - reader.Buffer.Length, "CborList", "array");
        }

        reader.ReadBeginArray();
        int count = reader.ReadSize();
        bool indefinite = count == -1;
        if (indefinite)
        {
            count = 0;
            while (!reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF)
            {
                reader.SkipDataItem();
                count++;
            }
            if (reader.Buffer.IsEmpty)
            {
                throw CborError.Decode(data, data.Length, "CborList", "end of indefinite array");
            }
        }
        else
        {
            // Validate the complete array so a break is not accepted as a definite array item.
            reader = new(data.Span);
            reader.SkipDataItem();
        }

        bytesConsumed = data.Length - reader.Buffer.Length + (indefinite ? 1 : 0);
        return new(data[..bytesConsumed], null, count, indefinite, tagged);
    }

    internal static void WriteCore(IBufferWriter<byte> output, CborList<T> value)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!value.Raw.IsEmpty)
        {
            output.Write(value.Raw.Span);
            return;
        }
        if (value._items is null)
        {
            throw CborError.Validation(-1, "CborList", "decoded list or list built from items", "default value");
        }

        CborWriter writer = new(output);
        if (value.HasSetTag)
        {
            writer.WriteSemanticTag(SetTag);
        }
        int size = value.IsIndefinite ? -1 : value.Count;
        writer.WriteBeginArray(size);
        foreach (T item in value._items)
        {
            GenericSerializationUtil.Write(output, item);
        }
        writer.WriteEndArray(size);
    }
}

/// <summary>Factory for CBOR arrays, with item type inference from the input sequence.</summary>
public static class CborList
{
    public static CborList<T> Create<T>(IEnumerable<T> items, bool isIndefinite = false, bool hasSetTag = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        return CborList<T>.FromItems(items, isIndefinite, hasSetTag);
    }
}
