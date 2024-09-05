using System.Reflection;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Coinecta.Vesting;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cardano.Models.Sundae;
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
    [InlineData("9f0102030405ff", typeof(CborIndefiniteList<CborInt>))] // [_ 1, 2, 3, 4, 5]
    [InlineData("850102030405", typeof(CborDefiniteList<CborInt>))] // [1, 2, 3, 4, 5]
    [InlineData("9f824401020304182a824405060708182bff", typeof(CborIndefiniteList<TransactionInput>))] // [_ [h'01020304', 42_0], [h'05060708', 43_0]]
    [InlineData("d8799f182aff", typeof(Option<CborInt>))] // Serialized CBOR for Option::Some(42):
    [InlineData("d87a80", typeof(Option<CborInt>))] // Serialized CBOR for Option::None:
    [InlineData("d8799f4180ff", typeof(Signature))] // Serialized CBOR for Signature:
    // [InlineData("d8799f460001020304051a000f4240ff", typeof(PostAlonzoTransactionOutput))] // Serialized CBOR for PostAlonzoTransactionOutput:
    [InlineData("d87c9f029fd8799f446b657931ffd8799f446b657932ffd8799f446b657933ffffff", typeof(AtLeast))] // Serialized CBOR for AtLeast Multisig:
    [InlineData("d8799fd8799f581ceca3dfbde8ccb8408cefacda690e34aa9353af93fc02e75d8ba42f1bff58202325f3c999b17d4a6399bf6c02e1ff7615c13a73ecafae7fe813b9757f27ef2600ff", typeof(Treasury))] // Serialized CBOR for Signature:
    [InlineData("8201d818587ed8799fd8799fd87a9f581c1eae96baf29e27682ea3f815aba361a0c6059d45e4bfbe95bbd2f44affffd8799f4040ffd8799f581caf65a4734e8a22f43128913567566d2dde30d3b3298306d6317570f64e0014df104d494e20496e7465726eff1a2a2597de1a009896801b0000000ba43b740018641864d87a80d87980ff", typeof(DatumOption))] // Serialized CBOR for Inline Datum:
    [InlineData("d81842ffff", typeof(CborEncodedValue))] // Serialized CBOR for CIP68:
    [InlineData("a300583911ea07b733d932129c378af627436e7cbc2ef0bf96e0036bb51b3bde6b52563c5410bff6a0d43ccebb7c37e1f69f5eb260552521adff33b9c201821a00dd40a0a2581caf65a4734e8a22f43128913567566d2dde30d3b3298306d6317570f6a14e0014df104d494e20496e7465726e1b0000000ba43b7400581cf5808c2c990d86da54bfc97d89cee6efa20cd8461616359478d96b4ca2434d5350015820e08460587b08cca542bd2856b8d5e1d23bf3f63f9916fb81f6d95fda0910bf691b7fffffffd5da682b028201d818587ed8799fd8799fd87a9f581c1eae96baf29e27682ea3f815aba361a0c6059d45e4bfbe95bbd2f44affffd8799f4040ffd8799f581caf65a4734e8a22f43128913567566d2dde30d3b3298306d6317570f64e0014df104d494e20496e7465726eff1a2a2597de1a009896801b0000000ba43b740018641864d87a80d87980ff", typeof(TransactionOutput))] // Serialized CBOR for TransactionOutput:
    [InlineData("D8799F9FD87B9F005820567463495E4DC4FB67268D9A6E92836A68A18A317D2F0CA6CC6D695EE7733889582023B4DA2A35E86C585A6F5FCCFF3B53F7660D73536C79FF486BCAB719B518C58FFFFFD8799FD8799F581CA7E1D2E57B1F9AA851B08C8934A315FFD97397FA997BB3851C626D3BFFA0A140A1401A05F5E10041004100FFFF", typeof(TreasuryRedeemer))] // Serialized CBOR for TreasuryRedeemer:
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
        Assert.Equal(cborHex.ToLowerInvariant(), serializedHex);
    }
}