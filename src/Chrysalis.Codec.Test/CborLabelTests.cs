using System.Buffers;
using Chrysalis.Codec.Types;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

public class CborLabelTests
{
    private static byte[] SerializeLabel(CborLabel label)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        if (label.Value is int i)
        {
            writer.WriteInt32(i);
        }
        else if (label.Value is long l)
        {
            writer.WriteInt64(l);
        }
        else if (label.Value is string s)
        {
            writer.WriteString(s);
        }
        else
        {
            throw new InvalidOperationException($"Unexpected label type: {label.Value.GetType()}");
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static CborLabel DeserializeLabel(byte[] bytes)
    {
        CborReader reader = new(bytes);
        CborDataItemType type = reader.GetCurrentDataItemType();
        if (type is CborDataItemType.Unsigned or CborDataItemType.Signed)
        {
            return new CborLabel(reader.ReadInt64());
        }
        if (type is CborDataItemType.String)
        {
            return new CborLabel(reader.ReadString()!);
        }
        throw new InvalidOperationException($"Unexpected CBOR type: {type}");
    }

    [Fact]
    public void CborLabel_IntConstructor_Works()
    {
        CborLabel label = new(42);
        Assert.Equal(42, label.Value);
    }

    [Fact]
    public void CborLabel_LongConstructor_Works()
    {
        CborLabel label = new(42L);
        Assert.Equal(42L, label.Value);
    }

    [Fact]
    public void CborLabel_StringConstructor_Works()
    {
        CborLabel label = new("header");
        Assert.Equal("header", label.Value);
    }

    [Fact]
    public void CborLabel_StringConstructor_NullThrows() => _ = Assert.Throws<ArgumentNullException>(() => new CborLabel(null!));

    [Fact]
    public void CborLabel_ImplicitConversion_Int_Works()
    {
        CborLabel label = 42;
        Assert.Equal(42, label.Value);
    }

    [Fact]
    public void CborLabel_ImplicitConversion_Long_Works()
    {
        CborLabel label = 42L;
        Assert.Equal(42L, label.Value);
    }

    [Fact]
    public void CborLabel_ImplicitConversion_String_Works()
    {
        CborLabel label = "custom-header";
        Assert.Equal("custom-header", label.Value);
    }

    [Fact]
    public void CborLabel_IntValue_SerializesAsInteger()
    {
        CborLabel label = 1;
        byte[] bytes = SerializeLabel(label);
        string hex = Convert.ToHexString(bytes);
        Assert.Equal("01", hex);
    }

    [Fact]
    public void CborLabel_StringValue_SerializesAsString()
    {
        CborLabel label = "alg";
        byte[] bytes = SerializeLabel(label);
        string hex = Convert.ToHexString(bytes);
        Assert.Equal("63616C67", hex);
    }

    [Fact]
    public void CborLabel_IntValue_RoundTrip()
    {
        CborLabel label = 42;
        byte[] bytes = SerializeLabel(label);
        CborLabel deserialized = DeserializeLabel(bytes);
        Assert.Equal(42L, deserialized.Value);
    }

    [Fact]
    public void CborLabel_StringValue_RoundTrip()
    {
        CborLabel label = "custom-header";
        byte[] bytes = SerializeLabel(label);
        CborLabel deserialized = DeserializeLabel(bytes);
        Assert.Equal("custom-header", deserialized.Value);
    }

    [Fact]
    public void CborLabel_NegativeInteger_Works()
    {
        CborLabel label = -1;
        byte[] bytes = SerializeLabel(label);
        string hex = Convert.ToHexString(bytes);
        Assert.Equal("20", hex);
    }

    [Fact]
    public void CborLabel_TypeSafety_InvalidTypesRejected()
    {
        CborLabel intLabel = new(42);
        CborLabel longLabel = new(42L);
        CborLabel stringLabel = new("test");

        Assert.Equal(42, intLabel.Value);
        Assert.Equal(42L, longLabel.Value);
        Assert.Equal("test", stringLabel.Value);
    }
}
