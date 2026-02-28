using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Addresses;

public class AddressTests
{
    // Well-known BIP39 test mnemonic
    private const string TestMnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    // Expected key hashes derived from TestMnemonic via m/1852'/1815'/0'
    private const string ExpectedPaymentHash = "0fdc780023d8be7c9ff3a6bdc0d8d3b263bd0cc12448c40948efbf42";
    private const string ExpectedStakeHash = "e557890352095f1cf6fd2b7d1a28e3c3cb029f48cf34ff890a28d176";

    // Expected mainnet addresses (network nibble = 1)
    private const string ExpectedMainnetBase = "addr1qy8ac7qqy0vtulyl7wntmsxc6wex80gvcyjy33qffrhm7sh927ysx5sftuw0dlft05dz3c7revpf7jx0xnlcjz3g69mq4afdhv";
    private const string ExpectedMainnetEnterprise = "addr1vy8ac7qqy0vtulyl7wntmsxc6wex80gvcyjy33qffrhm7ss7lxrqp";
    private const string ExpectedMainnetStake = "stake1u8j40zgr2gy4788kl54h6x3gu0pukq5lfr8nflufpg5dzaskqlx2l";

    // Expected testnet addresses (network nibble = 0, same for testnet/preview/preprod)
    private const string ExpectedTestnetBase = "addr_test1qq8ac7qqy0vtulyl7wntmsxc6wex80gvcyjy33qffrhm7sh927ysx5sftuw0dlft05dz3c7revpf7jx0xnlcjz3g69mqkt5dmn";
    private const string ExpectedTestnetEnterprise = "addr_test1vq8ac7qqy0vtulyl7wntmsxc6wex80gvcyjy33qffrhm7ss9hjl0y";
    private const string ExpectedTestnetStake = "stake_test1urj40zgr2gy4788kl54h6x3gu0pukq5lfr8nflufpg5dzas324ywz";

    private static (PublicKey payment, PublicKey stake) CreateTestKeyPair()
    {
        Mnemonic mnemonic = Mnemonic.Restore(TestMnemonic, English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        PrivateKey stakeKey = accountKey.Derive(RoleType.Staking).Derive(0);

        return (paymentKey.GetPublicKey(), stakeKey.GetPublicKey());
    }

    private static Address CreateStakeAddress(NetworkType network)
    {
        (_, PublicKey stakePub) = CreateTestKeyPair();
        return Address.FromPublicKeys(network, AddressType.Delegation, stakePub);
    }

    #region End-to-End Derivation: Key Hashes

    [Fact]
    public void Derivation_PaymentKeyHash_MatchesExpected()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();

        Assert.Equal(ExpectedPaymentHash, Convert.ToHexStringLower(HashUtil.Blake2b224(paymentPub.Key)));
    }

    [Fact]
    public void Derivation_StakeKeyHash_MatchesExpected()
    {
        (_, PublicKey stakePub) = CreateTestKeyPair();

        Assert.Equal(ExpectedStakeHash, Convert.ToHexStringLower(HashUtil.Blake2b224(stakePub.Key)));
    }

    #endregion

    #region End-to-End Derivation: Mainnet Addresses

    [Fact]
    public void Mainnet_BaseAddress_MatchesExpected()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal(ExpectedMainnetBase, address.ToBech32());
    }

    [Fact]
    public void Mainnet_BaseAddress_HasHeaderByte0x01()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal(0x01, address.ToBytes()[0]);
    }

    [Fact]
    public void Mainnet_EnterpriseAddress_MatchesExpected()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, paymentPub);

        Assert.Equal(ExpectedMainnetEnterprise, address.ToBech32());
    }

    [Fact]
    public void Mainnet_EnterpriseAddress_HasHeaderByte0x61()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, paymentPub);

        Assert.Equal(0x61, address.ToBytes()[0]);
    }

    [Fact]
    public void Mainnet_StakeAddress_MatchesExpected()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal(ExpectedMainnetStake, stakeAddr.ToBech32());
    }

    [Fact]
    public void Mainnet_StakeAddress_HasHeaderByte0xE1()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal(0xE1, stakeAddr.ToBytes()[0]);
    }

    [Fact]
    public void Mainnet_StakeAddress_MatchesExpectedHex()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal("e1" + ExpectedStakeHash, stakeAddr.ToHex());
    }

    [Fact]
    public void Mainnet_StakeAddress_Is29Bytes()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal(29, stakeAddr.ToBytes().Length);
    }

    #endregion

    #region End-to-End Derivation: Testnet Addresses

    [Fact]
    public void Testnet_BaseAddress_MatchesExpected()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal(ExpectedTestnetBase, address.ToBech32());
    }

    [Fact]
    public void Testnet_BaseAddress_HasHeaderByte0x00()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal(0x00, address.ToBytes()[0]);
    }

    [Fact]
    public void Testnet_EnterpriseAddress_MatchesExpected()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.EnterprisePayment, paymentPub);

        Assert.Equal(ExpectedTestnetEnterprise, address.ToBech32());
    }

    [Fact]
    public void Testnet_EnterpriseAddress_HasHeaderByte0x60()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.EnterprisePayment, paymentPub);

        Assert.Equal(0x60, address.ToBytes()[0]);
    }

    [Fact]
    public void Testnet_StakeAddress_MatchesExpected()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Testnet);

        Assert.Equal(ExpectedTestnetStake, stakeAddr.ToBech32());
    }

    [Fact]
    public void Testnet_StakeAddress_HasHeaderByte0xE0()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Testnet);

        Assert.Equal(0xE0, stakeAddr.ToBytes()[0]);
    }

    #endregion

    #region Preview/Preprod Produce Same Bytes as Testnet

    [Theory]
    [InlineData(NetworkType.Preview)]
    [InlineData(NetworkType.Preprod)]
    public void TestnetVariant_BaseAddress_MatchesTestnet(NetworkType networkType)
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(networkType, AddressType.Base, paymentPub, stakePub);

        Assert.Equal(ExpectedTestnetBase, address.ToBech32());
    }

    [Theory]
    [InlineData(NetworkType.Preview)]
    [InlineData(NetworkType.Preprod)]
    public void TestnetVariant_EnterpriseAddress_MatchesTestnet(NetworkType networkType)
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(networkType, AddressType.EnterprisePayment, paymentPub);

        Assert.Equal(ExpectedTestnetEnterprise, address.ToBech32());
    }

    [Theory]
    [InlineData(NetworkType.Preview)]
    [InlineData(NetworkType.Preprod)]
    public void TestnetVariant_StakeAddress_MatchesTestnet(NetworkType networkType)
    {
        Address stakeAddr = CreateStakeAddress(networkType);

        Assert.Equal(ExpectedTestnetStake, stakeAddr.ToBech32());
    }

    #endregion

    #region Bech32 Prefix Tests

    [Fact]
    public void Mainnet_BaseAddress_HasAddrPrefix()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal("addr", address.GetPrefix());
        Assert.StartsWith("addr1", address.ToBech32());
    }

    [Fact]
    public void Testnet_BaseAddress_HasAddrTestPrefix()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal("addr_test", address.GetPrefix());
        Assert.StartsWith("addr_test1", address.ToBech32());
    }

    [Fact]
    public void Mainnet_StakeAddress_HasStakePrefix()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal("stake", stakeAddr.GetPrefix());
        Assert.StartsWith("stake1", stakeAddr.ToBech32());
    }

    [Fact]
    public void Testnet_StakeAddress_HasStakeTestPrefix()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Testnet);

        Assert.Equal("stake_test", stakeAddr.GetPrefix());
        Assert.StartsWith("stake_test1", stakeAddr.ToBech32());
    }

    [Theory]
    [InlineData(NetworkType.Preview)]
    [InlineData(NetworkType.Preprod)]
    public void TestnetVariant_StakeAddress_HasStakeTestPrefix(NetworkType networkType)
    {
        Address stakeAddr = CreateStakeAddress(networkType);

        Assert.StartsWith("stake_test1", stakeAddr.ToBech32());
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void Mainnet_BaseAddress_Bech32RoundTrip()
    {
        Address parsed = Address.FromBech32(ExpectedMainnetBase);

        Assert.Equal(AddressType.Base, parsed.Type);
        Assert.Equal(ExpectedMainnetBase, parsed.ToBech32());
    }

    [Fact]
    public void Testnet_BaseAddress_Bech32RoundTrip()
    {
        Address parsed = Address.FromBech32(ExpectedTestnetBase);

        Assert.Equal(AddressType.Base, parsed.Type);
        Assert.Equal(ExpectedTestnetBase, parsed.ToBech32());
    }

    [Fact]
    public void Mainnet_StakeAddress_Bech32RoundTrip()
    {
        Address parsed = Address.FromBech32(ExpectedMainnetStake);

        Assert.Equal(AddressType.Delegation, parsed.Type);
        Assert.Equal(ExpectedMainnetStake, parsed.ToBech32());
    }

    [Fact]
    public void Testnet_StakeAddress_Bech32RoundTrip()
    {
        Address parsed = Address.FromBech32(ExpectedTestnetStake);

        Assert.Equal(AddressType.Delegation, parsed.Type);
        Assert.Equal(ExpectedTestnetStake, parsed.ToBech32());
    }

    [Fact]
    public void Mainnet_BaseAddress_BytesRoundTrip()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address original = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        Address parsed = Address.FromBytes(original.ToBytes());

        Assert.Equal(original.ToHex(), parsed.ToHex());
    }

    [Fact]
    public void Mainnet_StakeAddress_BytesRoundTrip()
    {
        Address original = CreateStakeAddress(NetworkType.Mainnet);

        Address parsed = Address.FromBytes(original.ToBytes());

        Assert.Equal(AddressType.Delegation, parsed.Type);
        Assert.Equal(original.ToHex(), parsed.ToHex());
    }

    [Fact]
    public void Testnet_StakeAddress_BytesRoundTrip()
    {
        Address original = CreateStakeAddress(NetworkType.Testnet);

        Address parsed = Address.FromBytes(original.ToBytes());

        Assert.Equal(AddressType.Delegation, parsed.Type);
        Assert.Equal(original.ToHex(), parsed.ToHex());
    }

    #endregion

    #region Credential Extraction

    [Fact]
    public void BaseAddress_GetPaymentKeyHash_Returns28Bytes()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        byte[]? paymentHash = address.GetPaymentKeyHash();

        Assert.NotNull(paymentHash);
        Assert.Equal(28, paymentHash.Length);
        Assert.Equal(ExpectedPaymentHash, Convert.ToHexStringLower(paymentHash));
    }

    [Fact]
    public void BaseAddress_GetStakeKeyHash_Returns28Bytes()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        byte[]? stakeHash = address.GetStakeKeyHash();

        Assert.NotNull(stakeHash);
        Assert.Equal(28, stakeHash.Length);
        Assert.Equal(ExpectedStakeHash, Convert.ToHexStringLower(stakeHash));
    }

    [Fact]
    public void EnterpriseAddress_GetStakeKeyHash_ReturnsNull()
    {
        (PublicKey paymentPub, _) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, paymentPub);

        Assert.Null(address.GetStakeKeyHash());
    }

    [Fact]
    public void StakeAddress_GetStakeKeyHash_MatchesExpected()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        byte[]? skh = stakeAddr.GetStakeKeyHash();

        Assert.NotNull(skh);
        Assert.Equal(ExpectedStakeHash, Convert.ToHexStringLower(skh));
    }

    [Fact]
    public void StakeAddress_GetPaymentKeyHash_ReturnsNull()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Null(stakeAddr.GetPaymentKeyHash());
    }

    [Fact]
    public void ParsedMainnetStakeAddress_GetStakeKeyHash_MatchesExpected()
    {
        Address parsed = Address.FromBech32(ExpectedMainnetStake);

        byte[]? skh = parsed.GetStakeKeyHash();

        Assert.NotNull(skh);
        Assert.Equal(ExpectedStakeHash, Convert.ToHexStringLower(skh));
    }

    [Fact]
    public void ParsedTestnetStakeAddress_GetStakeKeyHash_MatchesExpected()
    {
        Address parsed = Address.FromBech32(ExpectedTestnetStake);

        byte[]? skh = parsed.GetStakeKeyHash();

        Assert.NotNull(skh);
        Assert.Equal(ExpectedStakeHash, Convert.ToHexStringLower(skh));
    }

    #endregion

    #region Error Handling

    [Fact]
    public void GetAddressHeader_UnknownType_ThrowsArgumentOutOfRangeException()
    {
        // Header byte 0x81 has type nibble 8 which is not a valid CIP-19 type
        Assert.Throws<ArgumentOutOfRangeException>(() => Address.GetAddressHeader(0x81));
    }

    [Fact]
    public void ToHex_ReturnsValidHexString()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        string hex = address.ToHex();

        Assert.NotEmpty(hex);
        Assert.True(hex.All(c => "0123456789abcdef".Contains(c)));
    }

    #endregion
}
