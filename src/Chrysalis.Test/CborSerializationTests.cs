using System.Reflection;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Coinecta.Vesting;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Levvy;
using Chrysalis.Cardano.Models.Mpf;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cardano.Models.Sundae;
using Chrysalis.Cbor;
using Xunit;
using Address = Chrysalis.Cardano.Models.Plutus.Address;

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
    [InlineData("A300583911343F54D6ADFB256B1E9041A72A8B519670086570D5611DF499C797AA03EC6A12860EF8C07D4C1A8DE7DF06ACB0F0330A6087ECBE972082A7011A0BEBC200028201D818589CD8799FD8799FD8799FD8799F581CE63022B0F461602484968BB10FD8F872787B862ACE2D7E943292A370FFD8799FD8799FD8799F581C03EC6A12860EF8C07D4C1A8DE7DF06ACB0F0330A6087ECBE972082A7FFFFFFFF581C285B65AE63D4FAD36321384EC61EDFD5187B8194FFF89B5ABE9876DA46414E47454C531A0121EAC01A0BEBC2001A00B71B001A48190800D8799FD8799F4100FF00FFFFFF", typeof(TransactionOutput))] // Serialized CBOR for TransactionOutput:
    [InlineData("D8799F9FD87B9F005820567463495E4DC4FB67268D9A6E92836A68A18A317D2F0CA6CC6D695EE7733889582023B4DA2A35E86C585A6F5FCCFF3B53F7660D73536C79FF486BCAB719B518C58FFFFFD8799FD8799F581CA7E1D2E57B1F9AA851B08C8934A315FFD97397FA997BB3851C626D3BFFA0A140A1401A05F5E10041004100FFFF", typeof(TreasuryRedeemer))] // Serialized CBOR for TreasuryRedeemer:
    [InlineData("D8799FD8799F581CA7E1D2E57B1F9AA851B08C8934A315FFD97397FA997BB3851C626D3BFFA0A041004100FF", typeof(ClaimEntry))] // Serialized CBOR for ClaimEntry:
    [InlineData("9fd87a9f00d8799f0e405820907a68a571ed8634e16b1abac5a9765fce7a27b3cb11f0059f4755699be1702bffffff", typeof(Proof))]
    [InlineData("d8799fd8799fd8799fd8799f581ce63022b0f461602484968bb10fd8f872787b862ace2d7e943292a370ffd8799fd8799fd8799f581c03ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7ffffffff581c285b65ae63d4fad36321384ec61edfd5187b8194fff89b5abe9876da46414e47454c531a0112a8801a0bebc2001a00b71b001a48190800d8799fd8799f4100ff00ffffff", typeof(TokenDatum))]
    [InlineData("d8799fd8799f4100ff00ff", typeof(OutputReference))]
    [InlineData("d8799fd8799f581ce63022b0f461602484968bb10fd8f872787b862ace2d7e943292a370ffd8799fd8799fd8799f581c03ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7ffffffff", typeof(Address))]
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