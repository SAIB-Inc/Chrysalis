using System.Reflection;
using Chrysalis.Cardano.Models;
using Chrysalis.Cardano.Models.Coinecta;
using Chrysalis.Cbor;
using Xunit;

namespace Chrysalis.Test;

public class CborSerializerTests
{
    [Theory]
    [InlineData("1834", typeof(CborInt))] // Example hex for CBOR int 52
    [InlineData("4101", typeof(CborBytes))] // Example hex for CBOR bytes {0x01)]
    [InlineData("1a000f4240", typeof(CborUlong))] // Example hex for CBOR ulong 1_000_000
    [InlineData("1a000f4240", typeof(PosixTime))] // Example hex for CBOR ulong 1_000_000
    [InlineData("43414243", typeof(CborBytes))] // Example hex for CBOR bytes of `ABC` string
    [InlineData("a141614541696b656e", typeof(CborMap<CborBytes, CborBytes>))] // {h'61': h'41696b656e'}
    [InlineData("a1450001020304a1450001020304182a", typeof(MultiAsset))] // {h'61': h'41696b656e'}
    [InlineData("9f0102030405ff", typeof(CborList<CborInt>))] // [_ 1, 2, 3, 4, 5]
    [InlineData("9f824401020304182a824405060708182bff", typeof(CborList<TransactionInput>))] // [_ [h'01020304', 42_0], [h'05060708', 43_0]]
    [InlineData("d8799f182aff", typeof(Option<CborInt>))] // Serialized CBOR for Option::Some(42):
    [InlineData("d87a80", typeof(Option<CborInt>))] // Serialized CBOR for Option::None:
    [InlineData("d8799f4180ff", typeof(Signature))] // Serialized CBOR for Signature:
    // [InlineData("d8799f460001020304051a000f4240ff", typeof(PostAlonzoTransactionOutput))] // Serialized CBOR for PostAlonzoTransactionOutput:
    [InlineData("d87c9f029fd8799f446b657931ffd8799f446b657932ffd8799f446b657933ffffff", typeof(AtLeast))] // Serialized CBOR for AtLeast Multisig:
    [InlineData("d8799fd8799f581ceca3dfbde8ccb8408cefacda690e34aa9353af93fc02e75d8ba42f1bff58202325f3c999b17d4a6399bf6c02e1ff7615c13a73ecafae7fe813b9757f27ef2600ff", typeof(Treasury))] // Serialized CBOR for Signature:
    public void SerializeAndDeserializePrimitives(string cborHex, Type type)
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
        Assert.Equal(cborHex, serializedHex);
    }
}