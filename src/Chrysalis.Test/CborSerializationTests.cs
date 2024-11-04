using System.Reflection;
using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Plutus;
using Chrysalis.Cbor;
using Xunit;
using Credential = Chrysalis.Cardano.Plutus.Credential;

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

    [Theory]
    [InlineData("d8799fd87a9f581c1eae96baf29e27682ea3f815aba361a0c6059d45e4bfbe95bbd2f44affff", typeof(Referenced<Credential>))]
    public void SerializeAndDeserializeBlock(string cborHex, Type type)
    {
        SerializeAndDeserialize(cborHex, type);
    }

    [Theory]
    [InlineData("84a300d90102818258208af7d9c96301f58a748e3a6fe5f51580f083f2a30f20e559e2d4617dbeba7c5a00018182581d6033c378cee41b2e15ac848f7f6f1d2f78155ab12d93b713de898d855f1b000000012a035f4f021a000292b1a100d90102818258207463e1a9acb0d70f9b652ff2c125f5fbcf2288afbbbc1e3132fe18cacbfdba2f5840aef0bed9b035c3ffbf10acb4efc6cd059a9da49eaec197d8bd3770618fd5e04acb6607118b29ebff303ae02b95586c50376bc688fdd6d6d0677ee382680a5f02f5f6", typeof(Transaction))]
    public void SerializeAndDeserializeTransaction(string cborHex, Type type)
    {
        SerializeAndDeserialize(cborHex, type);
    }
}