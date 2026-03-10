using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Types;

namespace Chrysalis.Codec.V2.Test;

public class CborPrimitiveTests
{
    [Fact]
    public void CborPrimitive_Int_RoundTrip()
    {
        // CBOR encoding of integer 42: 0x18 0x2a
        byte[] bytes = [0x18, 0x2a];

        ICborPrimitive deserialized = CborSerializer.Deserialize<ICborPrimitive>(bytes);

        CborInt result = Assert.IsType<CborInt>(deserialized);
        Assert.Equal(42, result.Value);

        byte[] reserialized = CborSerializer.Serialize(result);
        Assert.Equal(bytes, reserialized);
    }

    [Fact]
    public void CborPrimitive_String_RoundTrip()
    {
        // CBOR encoding of "hello": 0x65 + UTF8 bytes
        byte[] bytes = [0x65, 0x68, 0x65, 0x6c, 0x6c, 0x6f];

        ICborPrimitive deserialized = CborSerializer.Deserialize<ICborPrimitive>(bytes);

        CborString result = Assert.IsType<CborString>(deserialized);
        Assert.Equal("hello", result.Value);

        byte[] reserialized = CborSerializer.Serialize(result);
        Assert.Equal(bytes, reserialized);
    }

    [Fact]
    public void CborPrimitive_Bool_RoundTrip()
    {
        // CBOR encoding of true: 0xf5
        byte[] bytes = [0xf5];

        ICborPrimitive deserialized = CborSerializer.Deserialize<ICborPrimitive>(bytes);

        CborBool result = Assert.IsType<CborBool>(deserialized);
        Assert.True(result.Value);

        byte[] reserialized = CborSerializer.Serialize(result);
        Assert.Equal(bytes, reserialized);
    }
}
