using Chrysalis.Wallet.Cli;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Test.Keys;

namespace Chrysalis.Wallet.Test;

public class WalletCliKeyFileTests
{
    // Test-only material, never a real key: cardano-cli's Shelley extended payment key goldens at tag cardano-cli-11.7.0.0,
    // commit 24c05829e854f9b2f56dcb9422bff5ff3ff60f77, https://github.com/IntersectMBO/cardano-cli, Apache-2.0
    // (licence and notice in src/Chrysalis.IntegrationTests/Golden/cardano-cli-11.7.0.0/). Under
    // cardano-cli/test/cardano-cli-golden/files/input/shelley/keys/extended_payment_keys/, with sha256:
    //   signing_key       989f64345503d8d770f558df221be0632443a3e5bb918e67b663836e02438cc0
    //   verification_key  db1c2c620cb1fd121b577389251f0e755ffa4de56fadf514671efcaf4b972e97
    // Standard 32-byte payment keys are reused from PrivateKeyTests.CliSeed and PrivateKeyTests.CliVerificationKey.
    internal const string ExtendedPrivateKeyHex =
        "c0eb138a32cde8de380312a08bae4383939520c84f8242f0262b54fa7f12724d"
        + "aa13fcf3663749f3b0fb231f02a63b46703d2a0904fb2f2ff383c1cce52af5af";
    internal const string ExtendedPublicKeyHex = "1f9591d2f4bef7008a549512ea2c51d78fe5954977045715303579bfdebfe1c3";
    internal const string ExtendedChainCodeHex = "4b3c13ea151f2652db80ea369878f2dd042aa78a1d17b9a78b941c856ea9ff21";
    private const string ExtendedSigningKeyCborHex = "5880" + ExtendedPrivateKeyHex + ExtendedPublicKeyHex + ExtendedChainCodeHex;
    private const string ExtendedVerificationKeyCborHex = "5840" + ExtendedPublicKeyHex + ExtendedChainCodeHex;

    [Theory]
    [InlineData("shelley-extended", "PaymentExtendedSigningKeyShelley_ed25519_bip32")]
    [InlineData("byron-extended", "PaymentExtendedSigningKeyByron_ed25519_bip32")]
    [InlineData("untyped-128", null)]
    public void ReadSigningKey_128ByteValidEnvelopes_ReadChainCodeAtOffset96(string caseName, string? type)
    {
        _ = caseName;
        string json = MakeEnvelope(type, ExtendedSigningKeyCborHex);

        PrivateKey privateKey = KeyFileReader.ReadSigningKey(json);

        Assert.Equal(Convert.FromHexString(ExtendedPrivateKeyHex), privateKey.Key);
        Assert.Equal(Convert.FromHexString(ExtendedChainCodeHex), privateKey.Chaincode);
        Assert.Equal(Convert.FromHexString(ExtendedPublicKeyHex), privateKey.GetPublicKey().Key);
    }

    [Theory]
    [InlineData("untyped-64", null)]
    [InlineData("extended-skey-type", "PaymentExtendedSigningKeyShelley_ed25519_bip32")]
    [InlineData("vrf-skey-type", "VrfSigningKey_PraosVRF")]
    [InlineData("extended-vkey-type", "PaymentExtendedVerificationKeyShelley_ed25519_bip32")]
    public void ReadSigningKey_64ByteEnvelope_ThrowsInvalidOperationException(string caseName, string? type)
    {
        _ = caseName;
        string json = MakeEnvelope(type, "5840" + ExtendedPrivateKeyHex);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => KeyFileReader.ReadSigningKey(json));

        Assert.Contains("VrfSigningKey_PraosVRF", error.Message, StringComparison.Ordinal);
        Assert.Contains("128-byte", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("typed-32-payment", "PaymentSigningKeyShelley_ed25519")]
    [InlineData("untyped-32", null)]
    public void ReadSigningKey_32ByteValidEnvelopes_LoadSeed(string caseName, string? type)
    {
        _ = caseName;
        string json = MakeEnvelope(type, "5820" + PrivateKeyTests.CliSeed);

        PrivateKey privateKey = KeyFileReader.ReadSigningKey(json);

        Assert.Equal(Convert.FromHexString(PrivateKeyTests.CliSeed), privateKey.Key);
        Assert.Empty(privateKey.Chaincode);
        Assert.Empty(privateKey.GetPublicKey().Chaincode);
        Assert.Equal(Convert.FromHexString(PrivateKeyTests.CliVerificationKey), privateKey.GetPublicKey().Key);
    }

    [Theory]
    [InlineData("vkey-as-skey", "PaymentVerificationKeyShelley_ed25519")]
    [InlineData("bls-skey", "SigningKey_bls12-381-g2")]
    [InlineData("kes-skey", "KesSigningKey_ed25519_kes_2^6")]
    [InlineData("vrf-skey-32", "VrfSigningKey_PraosVRF")]
    public void ReadSigningKey_WrongRoleOrNonEd25519Type_ThrowsInvalidOperationException(string caseName, string type)
    {
        _ = caseName;
        string json = MakeEnvelope(type, "5820" + PrivateKeyTests.CliSeed);

        _ = Assert.Throws<InvalidOperationException>(() => KeyFileReader.ReadSigningKey(json));
    }

    [Theory]
    [InlineData("empty-type")]
    [InlineData("null-type-property")]
    [InlineData("trailing-cbor")]
    public void ReadSigningKey_MalformedTypeFieldOrTrailingCbor_ThrowsInvalidOperationException(string caseName)
    {
        string json = caseName switch
        {
            "empty-type" => MakeEnvelope("   ", "5820" + PrivateKeyTests.CliSeed),
            "null-type-property" => $$"""{"type":null,"cborHex":"5820{{PrivateKeyTests.CliSeed}}"}""",
            "trailing-cbor" => MakeEnvelope("PaymentSigningKeyShelley_ed25519", "5820" + PrivateKeyTests.CliSeed + "00"),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };

        _ = Assert.Throws<InvalidOperationException>(() => KeyFileReader.ReadSigningKey(json));
    }

    [Theory]
    [InlineData("32-payment", "PaymentVerificationKeyShelley_ed25519", false)]
    [InlineData("32-byron-public-only", "PaymentVerificationKeyByron_ed25519_bip32", false)]
    [InlineData("32-generic-extended-public-only", "ExtendedVerificationKeyShelley_ed25519_bip32", false)]
    [InlineData("32-untyped", null, false)]
    [InlineData("64-shelley-extended", "PaymentExtendedVerificationKeyShelley_ed25519_bip32", true)]
    [InlineData("64-untyped", null, true)]
    public void ReadVerificationKey_Valid32And64ByteEnvelopes_LoadExpectedKey(string caseName, string? type, bool isExtended)
    {
        _ = caseName;
        string cborHex = isExtended ? ExtendedVerificationKeyCborHex : "5820" + PrivateKeyTests.CliVerificationKey;
        string json = MakeEnvelope(type, cborHex);

        PublicKey publicKey = KeyFileReader.ReadVerificationKey(json);

        byte[] expectedKey = Convert.FromHexString(isExtended ? ExtendedPublicKeyHex : PrivateKeyTests.CliVerificationKey);
        byte[] expectedChainCode = isExtended ? Convert.FromHexString(ExtendedChainCodeHex) : [];
        Assert.Equal(expectedKey, publicKey.Key);
        Assert.Equal(expectedChainCode, publicKey.Chaincode);
    }

    [Fact]
    public void ReadVerificationKey_32BytePlainEnvelope_RefusesPublicDerivation()
    {
        string json = MakeEnvelope("PaymentVerificationKeyShelley_ed25519", "5820" + PrivateKeyTests.CliVerificationKey);

        PublicKey publicKey = KeyFileReader.ReadVerificationKey(json);

        Assert.Empty(publicKey.Chaincode);
        _ = Assert.Throws<InvalidOperationException>(() => publicKey.Derive(0));
        _ = Assert.Throws<InvalidOperationException>(() => publicKey.DerivePaymentKey());
        _ = Assert.Throws<InvalidOperationException>(() => publicKey.DeriveStakeKey());
    }

    [Theory]
    [InlineData("skey-as-vkey", "PaymentSigningKeyShelley_ed25519")]
    [InlineData("vrf-vkey", "VrfVerificationKey_PraosVRF")]
    [InlineData("kes-vkey", "KesVerificationKey_ed25519_kes_2^6")]
    [InlineData("bls-vkey", "VerificationKey_bls12-381-g2")]
    public void ReadVerificationKey_WrongRoleOrNonEd25519Type_ThrowsInvalidOperationException(string caseName, string type)
    {
        _ = caseName;
        string json = MakeEnvelope(type, "5820" + PrivateKeyTests.CliVerificationKey);

        _ = Assert.Throws<InvalidOperationException>(() => KeyFileReader.ReadVerificationKey(json));
    }

    [Theory]
    [InlineData("40-bytes")]
    [InlineData("128-byte-skey-blob")]
    [InlineData("nonstring-type")]
    [InlineData("trailing-cbor")]
    public void ReadVerificationKey_BadLengthMalformedTypeOrTrailingCbor_ThrowsInvalidOperationException(string caseName)
    {
        string json = caseName switch
        {
            "40-bytes" => MakeEnvelope("PaymentVerificationKeyShelley_ed25519", "5828" + new string('0', 80)),
            "128-byte-skey-blob" => MakeEnvelope(null, ExtendedSigningKeyCborHex),
            "nonstring-type" => $$"""{"type":123,"cborHex":"5820{{PrivateKeyTests.CliVerificationKey}}"}""",
            "trailing-cbor" => MakeEnvelope("PaymentVerificationKeyShelley_ed25519", "5820" + PrivateKeyTests.CliVerificationKey + "00"),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };

        _ = Assert.Throws<InvalidOperationException>(() => KeyFileReader.ReadVerificationKey(json));
    }

    private static string MakeEnvelope(string? type, string cborHex) =>
        type is null
            ? $$"""{"cborHex":"{{cborHex}}"}"""
            : $$"""{"type":"{{type}}","cborHex":"{{cborHex}}"}""";
}
