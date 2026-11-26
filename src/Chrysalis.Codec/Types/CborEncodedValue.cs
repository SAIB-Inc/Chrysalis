using System.Buffers;
using Chrysalis.Codec.Serialization;

namespace Chrysalis.Codec.Types;

/// <summary>Embedded CBOR stored as a tag 24 byte string on fresh writes.</summary>
public readonly partial record struct CborEncodedValue : ICborType
{
    /// <summary>The inner CBOR bytes, without the tag and byte string headers.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; }

    /// <inheritdoc />
    public int ConstrIndex => 0;

    /// <inheritdoc />
    public bool IsIndefinite => false;

    /// <summary>Wraps the supplied memory without copying or encoding it.</summary>
    public CborEncodedValue(ReadOnlyMemory<byte> value) : this(value, default) { }

    private CborEncodedValue(ReadOnlyMemory<byte> value, ReadOnlyMemory<byte> raw)
    {
        Value = value;
        Raw = raw;
    }

    /// <summary>Reads the first item, borrowing a definite byte string or joining indefinite chunks.</summary>
    public static CborEncodedValue Read(ReadOnlyMemory<byte> data) => Read(data, out _);

    /// <summary>Reads the first item and reports its encoded length.</summary>
    public static CborEncodedValue Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        ByteStringWire.Read(data, "Chrysalis.Codec.Types.CborEncodedValue.Value", out ReadOnlyMemory<byte> value, out bytesConsumed);
        return new(value, data[..bytesConsumed]);
    }

    /// <summary>Writes the original wire bytes when available, otherwise encodes the value.</summary>
    public static void Write(IBufferWriter<byte> output, CborEncodedValue data) => ByteStringWire.Write(output, data.Raw, data.Value, tag24: true);

    /// <summary>Encodes the value into newly owned storage.</summary>
    public static CborEncodedValue Create(ReadOnlyMemory<byte> value) => Read(ByteStringWire.Encode(value, tag24: true));
}
