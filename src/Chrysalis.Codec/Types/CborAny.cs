using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

/// <summary>
/// Represents any raw CBOR data item. Stores bytes verbatim and writes
/// them back as-is — no tag wrapping, no bytestring encoding.
/// Use <see cref="To{T}"/> to deserialize into a concrete <see cref="ICborType"/>.
/// The codegen has a special case for this type: write outputs Raw directly,
/// read captures a single CBOR data item into Raw.
/// </summary>
[CborSerializable]
public partial record CborAny : ICborType
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; set; }

    /// <inheritdoc />
    public int ConstrIndex { get; set; }

    /// <inheritdoc />
    public bool IsIndefinite { get; set; }

    /// <summary>Creates a CborAny from raw CBOR bytes.</summary>
    public CborAny(ReadOnlyMemory<byte> raw) => Raw = raw;

    /// <summary>Deserializes the raw CBOR into a concrete type.</summary>
    public T To<T>() where T : ICborType => CborSerializer.Deserialize<T>(Raw);

    /// <summary>Creates a CborAny by serializing a typed value.</summary>
    public static CborAny From<T>(T value) where T : ICborType => new(CborSerializer.Serialize(value));

    /// <summary>Creates a CborAny from raw CBOR bytes.</summary>
    public static CborAny FromRaw(ReadOnlyMemory<byte> raw) => new(raw);

    /// <summary>Creates a CborAny by encoding a CBOR primitive via a writer action.</summary>
    public static CborAny FromPrimitive(Action<SAIB.Cbor.Serialization.CborWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArrayBufferWriter<byte> buf = new();
        SAIB.Cbor.Serialization.CborWriter w = new(buf);
        write(w);
        return new CborAny(buf.WrittenMemory.ToArray());
    }
}
