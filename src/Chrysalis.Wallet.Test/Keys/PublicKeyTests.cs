using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Keys;

public class PublicKeyTests
{
    // Test-only material, never a real key: Shelley HD root, account, payment xpub, and address literals
    // from IntersectMBO/cardano-addresses tag 4.1.0, commit ef9368855c0ffe552530058c5df04db38529f475,
    // https://github.com/IntersectMBO/cardano-addresses, Apache-2.0 (licence and notice beside this file:
    // LICENSE-cardano-addresses and NOTICE-cardano-addresses). File test/golden/addresses_0d256c78/golden,
    // sha256 85336fb500dd295802bbd19080ad4efcaf7f856887c4b0f03112b5c892602943.
    internal const string RootXPrv =
        "root_xsk17zqw352yj02seytp9apunec55722k93crtplq8chgpfh7cx33dg4v2x3wpyhd9chhkknzhprztumrystkpfl5nyhyeuq0gnwf76r39u9l9q3z40hgf5jv6xn8unr5acs3yy8fxg35v5xjsw4kwvf5zfkvcn57fle";
    internal const string AccXPrv0 =
        "acct_xsk13rkczfa4xzrax4x0unqnesaasr8s3qnyr2mmjcwga6lrue733dg6at2stlqft9jtz0kk50xsp4yrqnrf9gl78npu50r7e38kpu0f744af46s2r37d7m6sh0uyj9szp86s546k43t2thfw4nzd7zsxckx6gd7zxnq";
    internal const string AccXPrv1 =
        "acct_xsk1rrd6a79s9gktygfmp4gxhme3jxvnxazk45u2twrwfnfg2c733dg323eaegqrxe7lt0zepz3aq3ydgdse8vjs7qks4c4yh0q39pdf4fsv4ql5tv5tny3mxzn20r792nl9nk904y0ztqgclyxuxzl56p6fsydppkqk";
    private const string AddrXPub0 =
        "addr_xvk1x4dme9s2f5xxn77wgjhggqh73r6syy4nvjcdjklnaqrh48f6desjgf50dg5jww9gc30j9mhzl3zr3en4mjaltkpel4xempqdln80rjq5grmc7";
    private const string AddrXPub1 =
        "addr_xvk1ndtepmpg06x9nskfasvr50mue356e4rqlvuzf8jjcj6n48feexsg7nsjyplmsky60snfh2kreugf8gdgw6d5q77mr5494jl5swwr0ysprauul";
    private const string AddrXPub1442 =
        "addr_xvk1vagd70p45qd774cdna2qe2t3t9200hx3jcfvp39w3r9ruscjapdncx7hc2mcpjk5ydjqnkt97h532unrm7k8czquss8x7ly7nr5c06q2am4uq";

    internal static byte[] DecodeBech32Key(string bech32) => Bech32Util.Decode(bech32).data;

    private static PrivateKey DecodePrivateKey(string bech32)
    {
        byte[] raw = DecodeBech32Key(bech32);
        return new PrivateKey(raw[..64], raw[64..]);
    }

    private static PublicKey DecodePublicKey(string bech32)
    {
        byte[] raw = DecodeBech32Key(bech32);
        return new PublicKey(raw[..32], raw[32..]);
    }

    private static PublicKey CreateTestPublicKey()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            TestVectors.Bip39ZeroEntropy12,
            English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();
        return rootKey.GetPublicKey();
    }

    [Fact]
    public void GetPublicKey_Returns32ByteKey()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.Equal(32, publicKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_Returns32ByteChaincode()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.Equal(32, publicKey.Chaincode.Length);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            TestVectors.Bip39ZeroEntropy12,
            English.Words);
        PrivateKey privateKey = mnemonic.GetRootKey();
        PublicKey publicKey = privateKey.GetPublicKey();

        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] signature = privateKey.Sign(message);

        Assert.True(publicKey.Verify(message, signature));
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        PublicKey publicKey = CreateTestPublicKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] invalidSignature = new byte[64];

        Assert.False(publicKey.Verify(message, invalidSignature));
    }

    [Fact]
    public void Verify_WrongMessage_ReturnsFalse()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            TestVectors.Bip39ZeroEntropy12,
            English.Words);
        PrivateKey privateKey = mnemonic.GetRootKey();
        PublicKey publicKey = privateKey.GetPublicKey();

        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] wrongMessage = "Wrong message"u8.ToArray();
        byte[] signature = privateKey.Sign(message);

        Assert.False(publicKey.Verify(wrongMessage, signature));
    }

    [Fact]
    public void ToHex_ReturnsValidHexString()
    {
        PublicKey publicKey = CreateTestPublicKey();

        string hex = publicKey.ToHex();

        Assert.Equal(64, hex.Length);
        Assert.True(hex.All(c => "0123456789ABCDEF".Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public void ToBlake2b224_Returns28Bytes()
    {
        PublicKey publicKey = CreateTestPublicKey();

        byte[] hash = publicKey.ToBlake2b224();

        Assert.Equal(28, hash.Length);
    }

    [Fact]
    public void ToBlake2b224_SameKey_ReturnsSameHash()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.Equal(publicKey1.ToBlake2b224(), publicKey2.ToBlake2b224());
    }

    [Fact]
    public void Equals_SamePublicKey_ReturnsTrue()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.True(publicKey1.Equals(publicKey2));
    }

    [Fact]
    public void Equals_DifferentPublicKey_ReturnsFalse()
    {
        Mnemonic mnemonic1 = Mnemonic.Generate(English.Words, 12);
        Mnemonic mnemonic2 = Mnemonic.Generate(English.Words, 12);

        PrivateKey privateKey1 = mnemonic1.GetRootKey();
        PrivateKey privateKey2 = mnemonic2.GetRootKey();

        PublicKey publicKey1 = privateKey1.GetPublicKey();
        PublicKey publicKey2 = privateKey2.GetPublicKey();

        Assert.False(publicKey1.Equals(publicKey2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.False(publicKey.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameKey_ReturnsSameHash()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.Equal(publicKey1.GetHashCode(), publicKey2.GetHashCode());
    }

    [Theory]
    [InlineData(0, AddrXPub0, "addr_test1vz83dnlqqtdrlct4kz3f7d07d59w6p4yrtlr62340yklhaqrrykc7")]
    [InlineData(1, AddrXPub1, "addr_test1vzr08acccp7s3l9cppvptz7jyflejkkuma2k06vx4vjrcqsl4gkk5")]
    [InlineData(1442, AddrXPub1442, "addr_test1vz54amjcp4yxynrj877j4nk0umg5w0rf9t5lz3rfprxwjnses3aps")]
    public void DerivePaymentKey_MatchesCardanoAddresses410Vectors(int index, string expectedXPubBech32, string expectedEnterpriseAddress)
    {
        PrivateKey accPriv = DecodePrivateKey(AccXPrv0);
        PublicKey accPub = accPriv.GetPublicKey();
        PublicKey expectedXPub = DecodePublicKey(expectedXPubBech32);

        PublicKey derived = accPub.DerivePaymentKey(index);
        PublicKey fromPriv = accPriv.DerivePaymentKey(index).GetPublicKey();

        Assert.Equal(expectedXPub.Key, derived.Key);
        Assert.Equal(expectedXPub.Chaincode, derived.Chaincode);
        Assert.Equal(fromPriv.Key, derived.Key);
        Assert.Equal(fromPriv.Chaincode, derived.Chaincode);
        Assert.Equal(expectedEnterpriseAddress, Address.FromPublicKeys(NetworkType.Testnet, AddressType.EnterprisePayment, derived).ToBech32());
        if (index == 0)
        {
            Assert.Equal(derived, accPub.DerivePaymentKey());
        }
    }

    [Theory]
    [InlineData(0, 0, "addr_test1qz83dnlqqtdrlct4kz3f7d07d59w6p4yrtlr62340yklhaxcd4azvtus2m6m3q409pnflcurpnkz3gnxf4ef47ducezsh8t7e5")]
    [InlineData(1, 0, "addr_test1qzr08acccp7s3l9cppvptz7jyflejkkuma2k06vx4vjrcqkcd4azvtus2m6m3q409pnflcurpnkz3gnxf4ef47ducezs9qrgxg")]
    [InlineData(1442, 0, "addr_test1qz54amjcp4yxynrj877j4nk0umg5w0rf9t5lz3rfprxwjnkcd4azvtus2m6m3q409pnflcurpnkz3gnxf4ef47ducezstz0xhp")]
    [InlineData(0, 1, "addr_test1qz83dnlqqtdrlct4kz3f7d07d59w6p4yrtlr62340yklhay52juen6vgkzm3s3lzwefszuhhy56rekx68xv33n76a56s4xe03d")]
    public void DeriveStakeKey_MatchesCardanoAddresses410BaseAddresses(int paymentIndex, int stakeAccount, string expectedBaseAddress)
    {
        PrivateKey paymentAccPriv = DecodePrivateKey(AccXPrv0);
        PrivateKey stakeAccPriv = DecodePrivateKey(stakeAccount == 0 ? AccXPrv0 : AccXPrv1);

        PublicKey paymentPub = paymentAccPriv.GetPublicKey().DerivePaymentKey(paymentIndex);
        PublicKey stakePub = stakeAccPriv.GetPublicKey().DeriveStakeKey();
        PublicKey stakeFromPriv = stakeAccPriv.DeriveStakeKey().GetPublicKey();

        Assert.Equal(stakeFromPriv.Key, stakePub.Key);
        Assert.Equal(stakeFromPriv.Chaincode, stakePub.Chaincode);
        Assert.Equal(expectedBaseAddress, Address.FromPublicKeys(NetworkType.Testnet, AddressType.Base, paymentPub, stakePub).ToBech32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void Derive_CardanoCliExtendedKey_MatchesPrivateDerivationAndVerifiesSignature(int index)
    {
        PrivateKey cliPriv = new(Convert.FromHexString(WalletCliKeyFileTests.ExtendedPrivateKeyHex), Convert.FromHexString(WalletCliKeyFileTests.ExtendedChainCodeHex));
        PublicKey cliPub = new(Convert.FromHexString(WalletCliKeyFileTests.ExtendedPublicKeyHex), Convert.FromHexString(WalletCliKeyFileTests.ExtendedChainCodeHex));
        byte[] message = "wa4-cli-soft-derive"u8.ToArray();

        PrivateKey childPriv = cliPriv.Derive(index, DerivationType.SOFT);
        PublicKey childFromPriv = childPriv.GetPublicKey();
        PublicKey childPub = cliPub.Derive(index);

        Assert.Equal(childFromPriv.Key, childPub.Key);
        Assert.Equal(childFromPriv.Chaincode, childPub.Chaincode);
        Assert.Equal(
            Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, childFromPriv).ToBech32(),
            Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, childPub).ToBech32());
        Assert.True(childPub.Verify(message, childPriv.Sign(message)));
    }

    [Theory]
    [InlineData(RoleType.ExternalChain)]
    [InlineData(RoleType.InternalChain)]
    [InlineData(RoleType.Staking)]
    [InlineData(RoleType.DRep)]
    [InlineData(RoleType.ConstitutionalCommitteeColdKey)]
    [InlineData(RoleType.ConstitutionalCommitteeHotKey)]
    public void Derive_AllCip1852Roles_MatchesPrivateDerivation(RoleType role)
    {
        PrivateKey accPriv = DecodePrivateKey(AccXPrv0);
        PublicKey accPub = accPriv.GetPublicKey();
        byte[] message = "wa4-role-soft-derive"u8.ToArray();

        PrivateKey rolePriv = accPriv.Derive(role);
        PublicKey rolePub = accPub.Derive(role);
        PrivateKey leafPriv = rolePriv.Derive(0);
        PublicKey leafPub = rolePub.Derive(0);

        Assert.Equal(rolePriv.GetPublicKey().Key, rolePub.Key);
        Assert.Equal(rolePriv.Chaincode, rolePub.Chaincode);
        Assert.Equal(leafPriv.GetPublicKey().Key, leafPub.Key);
        Assert.Equal(leafPriv.Chaincode, leafPub.Chaincode);
        Assert.True(leafPub.Verify(message, leafPriv.Sign(message)));
    }

    [Fact]
    public void Derive_MultiLevelAndBoundaryIndex_MatchesPrivateDerivation()
    {
        PrivateKey accPriv = DecodePrivateKey(AccXPrv0);
        PublicKey accPub = accPriv.GetPublicKey();
        byte[] message = "wa4-multilevel-boundary"u8.ToArray();

        PrivateKey leafPriv = accPriv.Derive(0).Derive(int.MaxValue).Derive(1).Derive(0);
        PublicKey leafPub = accPub.Derive(0).Derive(int.MaxValue).Derive(1).Derive(0);

        Assert.Equal(leafPriv.GetPublicKey().Key, leafPub.Key);
        Assert.Equal(leafPriv.Chaincode, leafPub.Chaincode);
        Assert.True(leafPub.Verify(message, leafPriv.Sign(message)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Derive_NegativeIndex_ThrowsArgumentOutOfRangeException(int index)
    {
        PublicKey accPub = DecodePrivateKey(AccXPrv0).GetPublicKey();

        _ = Assert.Throws<ArgumentOutOfRangeException>(nameof(index), () => accPub.Derive(index));
    }

    [Theory]
    [InlineData("payment-negative", "index")]
    [InlineData("stake-negative", "index")]
    [InlineData("role-negative", "roleType")]
    [InlineData("role-undefined-positive", "roleType")]
    public void DeriveHelpers_InvalidArgument_ThrowsArgumentOutOfRangeException(string caseName, string expectedParam)
    {
        PublicKey accPub = DecodePrivateKey(AccXPrv0).GetPublicKey();

        Func<object?> action = caseName switch
        {
            "payment-negative" => () => accPub.DerivePaymentKey(-1),
            "stake-negative" => () => accPub.DeriveStakeKey(-1),
            "role-negative" => () => accPub.Derive((RoleType)(-1)),
            "role-undefined-positive" => () => accPub.Derive((RoleType)6),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };

        _ = Assert.Throws<ArgumentOutOfRangeException>(expectedParam, action);
    }

    [Theory]
    [InlineData("null-key", null, 32)]
    [InlineData("empty-key", 0, 32)]
    [InlineData("short-key", 31, 32)]
    [InlineData("long-key", 33, 32)]
    [InlineData("null-chaincode", 32, null)]
    [InlineData("empty-chaincode", 32, 0)]
    [InlineData("short-chaincode", 32, 31)]
    [InlineData("long-chaincode", 32, 33)]
    public void Derive_InvalidKeyOrChaincodeLengthOrNull_ThrowsInvalidOperationException(string caseName, int? keyLength, int? chaincodeLength)
    {
        _ = caseName;
        PublicKey valid = DecodePrivateKey(AccXPrv0).GetPublicKey();
        byte[] key = keyLength is null ? null! : keyLength == 32 ? valid.Key : new byte[keyLength.Value];
        byte[] chaincode = chaincodeLength is null ? null! : chaincodeLength == 32 ? valid.Chaincode : new byte[chaincodeLength.Value];

        _ = Assert.Throws<InvalidOperationException>(() => new PublicKey(key, chaincode).Derive(0));
    }

    [Theory]
    [InlineData("off-curve-y2", "0200000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("unreduced-p", "edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("unreduced-p1", "eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("noncanonical-x0-y1", "0100000000000000000000000000000000000000000000000000000000000080")]
    public void Derive_InvalidCurvePointKey_ThrowsInvalidOperationException(string caseName, string invalidPointHex)
    {
        _ = caseName;
        byte[] chaincode = DecodePrivateKey(AccXPrv0).Chaincode;
        PublicKey malformed = new(Convert.FromHexString(invalidPointHex), chaincode);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => malformed.Derive(0));

        _ = Assert.IsType<ArgumentException>(error.InnerException);
    }

    [Fact]
    public void Derive_FromSeedPublicKey_ThrowsInvalidOperationException()
    {
        byte[] seed = Convert.FromHexString(PrivateKeyTests.CliSeed);
        PublicKey fromZeroCc = new PrivateKey(seed, new byte[32]).GetPublicKey();
        PublicKey fromNonZeroCc = new PrivateKey(seed, [.. Enumerable.Repeat((byte)0x5A, 32)]).GetPublicKey();

        Assert.Empty(fromZeroCc.Chaincode);
        Assert.Empty(fromNonZeroCc.Chaincode);
        _ = Assert.Throws<InvalidOperationException>(() => fromZeroCc.Derive(0));
        _ = Assert.Throws<InvalidOperationException>(() => fromNonZeroCc.Derive(0));
    }

    [Fact]
    public void Derive_DoesNotMutateParentKeyOrChaincode()
    {
        PublicKey parent = DecodePrivateKey(AccXPrv0).GetPublicKey();
        byte[] originalKey = [.. parent.Key];
        byte[] originalChaincode = [.. parent.Chaincode];

        PublicKey child = parent.Derive(0);
        PublicKey payment = parent.DerivePaymentKey(1);
        PublicKey stake = parent.DeriveStakeKey(0);

        Assert.Equal(originalKey, parent.Key);
        Assert.Equal(originalChaincode, parent.Chaincode);
        Assert.NotEqual(originalKey, child.Key);
        Assert.NotEqual(originalKey, payment.Key);
        Assert.NotEqual(originalKey, stake.Key);
    }
}
