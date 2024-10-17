using System.Reflection;
using Chrysalis.Cbor;
using Xunit;

namespace Chrysalis.Test;

public class CborSerializerTests
{
    private static void SerializeAndDeserialize(string cborHex, Type type)
    {
        // Arrange
        byte[] cborBytes = Convert.FromHexString(cborHex);

        // Act
        MethodInfo? deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize));
        Assert.NotNull(deserializeMethod);

        MethodInfo? genericDeserializeMethod = deserializeMethod?.MakeGenericMethod(type);
        Assert.NotNull(genericDeserializeMethod);

        object? cborObject = genericDeserializeMethod?.Invoke(null, [cborBytes]);
        Assert.NotNull(cborObject);

        byte[] serializedBytes = CborSerializer.Serialize((ICbor)cborObject);
        string serializedHex = Convert.ToHexString(serializedBytes).ToLowerInvariant();

        // Assert
        Assert.Equal(cborHex.ToLowerInvariant(), serializedHex);
    }
    
    [Theory]
    [MemberData(nameof(CborTestData.PrimitiveTestData), MemberType = typeof(CborTestData))]
    public void SerializeAndDeserializePrimitive(string cborHex, Type type)
    {
        SerializeAndDeserialize(cborHex, type);
    }
    
    [Theory]
    [MemberData(nameof(CborTestData.JPegOfferTestData), MemberType = typeof(CborTestData))]
    public void SerializeAndDeserializeJpegOffer(string cborHex, Type type)
    {
        SerializeAndDeserialize(cborHex, type);
    }
    
    [Theory]
    [MemberData(nameof(CborTestData.BlockWithEraTestData), MemberType = typeof(CborTestData))]
    public void SerializeAndDeserializeBlockWithEra(string cborHex, Type type)
    {
        SerializeAndDeserialize(cborHex, type);
    }
}