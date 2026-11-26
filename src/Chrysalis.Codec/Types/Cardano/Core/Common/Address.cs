using System.Buffers;
using Chrysalis.Codec.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

/// <summary>A Cardano address stored as a CBOR byte string.</summary>
public readonly partial record struct Address : ICborType
{
    /// <summary>The address bytes.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; }

    /// <inheritdoc />
    public int ConstrIndex => 0;

    /// <inheritdoc />
    public bool IsIndefinite => false;

    /// <summary>Wraps the supplied memory without copying or encoding it.</summary>
    public Address(ReadOnlyMemory<byte> value) : this(value, default) { }

    private Address(ReadOnlyMemory<byte> value, ReadOnlyMemory<byte> raw)
    {
        Value = value;
        Raw = raw;
    }

    /// <summary>Reads the first item, borrowing a definite byte string or joining indefinite chunks.</summary>
    public static Address Read(ReadOnlyMemory<byte> data) => Read(data, out _);

    /// <summary>Reads the first item and reports its encoded length.</summary>
    public static Address Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        ByteStringWire.Read(data, "Chrysalis.Codec.Types.Cardano.Core.Common.Address.Value", out ReadOnlyMemory<byte> value, out bytesConsumed);
        return new(value, data[..bytesConsumed]);
    }

    /// <summary>Writes the original wire bytes when available, otherwise encodes the value.</summary>
    public static void Write(IBufferWriter<byte> output, Address data) => ByteStringWire.Write(output, data.Raw, data.Value, tag24: false);

    /// <summary>Encodes the value into newly owned storage.</summary>
    public static Address Create(ReadOnlyMemory<byte> value) => Read(ByteStringWire.Encode(value, tag24: false));
}
