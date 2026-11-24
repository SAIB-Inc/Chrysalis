using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Addresses;

public class AddressTests
{
    // Expected key hashes derived from the test vector via m/1852'/1815'/0'
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

    // Test-only: the CIP-0019 test vectors ("Cardano Addresses", Matthias Benkort, CC-BY-4.0; licence text in
    // TestData/CIPs/LICENSE.txt), https://github.com/cardano-foundation/CIPs at a4068a577790e7b17d2d6f00f6e54990cfcb7955,
    // CIP-0019/README.md (sha256 9fbc8c69f261650b5ccab975fe1e291358a2e5e09088690b000013ea1cb72ce5): the payment key,
    // stake key and script of :190-192 and the twenty addresses of :197-218, unchanged.
    private const string Cip19PaymentKey = "addr_vk1w0l2sr2zgfm26ztc6nl9xy8ghsk5sh6ldwemlpmp9xylzy4dtf7st80zhd";
    private const string Cip19StakeKey = "stake_vk1px4j0r2fk7ux5p23shz8f3y5y2qam7s954rgf3lg5merqcj6aetsft99wu";
    private const string Cip19Script = "script1cda3khwqv60360rp5m7akt50m6ttapacs8rqhn5w342z7r35m37";
    private const string Cip19MainnetEnterpriseScript = "addr1w8phkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtcyjy7wx";

    private const string Cip19MainnetKeyPointer = "addr1gx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer5pnz75xxcrzqf96k";
    private const string Cip19MainnetScriptPointer = "addr128phkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtupnz75xxcrtw79hu";
    private const string Cip19TestnetKeyPointer = "addr_test1gz2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer5pnz75xxcrdw5vky";
    private const string Cip19TestnetScriptPointer = "addr_test12rphkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtupnz75xxcryqrvmw";

    // CIP-19's pointer (2498243, 27, 3) (:193) as the ledger writes it: each number in 7-bit groups, the high bit set on
    // all but the last (cardano-ledger 95b369053f, libs/cardano-ledger-core/src/Cardano/Ledger/Address.hs:347-351, :372-373).
    private const string Cip19Pointer = "8198bd431b03";

    // Test-only: cardano-cli's golden test hprop_golden_shelleyAllMultiSigAddressBuild
    // (cardano-cli/test/cardano-cli-golden/Test/Golden/Shelley/MultiSig/Address.hs:10-30) at tag cardano-cli-11.7.0.0,
    // commit 24c05829e854f9b2f56dcb9422bff5ff3ff60f77, https://github.com/IntersectMBO/cardano-cli, Apache-2.0
    // (licence and notice in src/Chrysalis.IntegrationTests/Golden/cardano-cli-11.7.0.0/). Under
    // cardano-cli/test/cardano-cli-golden/files/input/shelley/multisig/, with sha256:
    //   scripts/all    2ee1d4cec920cd60df8900b74e0aebcebe8d9c5bcadfb83d610faeb963a566a0
    //   addresses/all  fa578d29610c77486e664720a95a157104bbd5ada8e24920336e4c5f2e80f388
    // CliAllKeyHashes are the eight "sig" key hashes of scripts/all, in order; CliAllAddress is addresses/all, which
    // `address build --payment-script-file scripts/all --mainnet` must print.
    private static readonly string[] CliAllKeyHashes =
    [
        "e09d36c79dec9bd1b3d9e152247701cd0bb860b5ebfd1de8abb6735a",
        "a687dcc24e00dd3caafbeb5e68f97ca8ef269cb6fe971345eb951756",
        "0bd1d702b2e6188fe0857a6dc7ffb0675229bab58c86638ffa87ed6d",
        "dd0044a26cf7d4491ecea720fda11afb59d5725b53afa605fdf695e6",
        "cf223afe150cc8e89f11edaacbbd55b011ba44fbedef66fbd37d8c9d",
        "372643e7ef4b41fd2649ada30a89d35cb90b7c14cb5de252e6ce6cb7",
        "aa453dc184c5037d60e3fbbadb023f4a41bac112f249b76be9bb37ad",
        "6b732c60c267bab894854d6dd57a04a94e603fcc4c36274c9ed75952",
    ];
    private const string CliAllAddress = "addr1w8ffpllpuslgpva4dz3r5r6nyw6mjt6mngemasl53mkk2hs3gad69";

    private static (PublicKey payment, PublicKey stake) CreateTestKeyPair()
    {
        Mnemonic mnemonic = Mnemonic.Restore(TestVectors.Bip39ZeroEntropy12, English.Words);
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

    // A CIP-19 part by name: the payment key's hash, the stake key's hash, the script hash, the pointer, or none ("").
    private static byte[] Cip19Part(string name) => name switch
    {
        "key" => HashUtil.Blake2b224(Bech32Util.Decode(Cip19PaymentKey).data),
        "stake" => HashUtil.Blake2b224(Bech32Util.Decode(Cip19StakeKey).data),
        "script" => Bech32Util.Decode(Cip19Script).data,
        "pointer" => Convert.FromHexString(Cip19Pointer),
        "" => [],
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

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

    #region CIP-19 and cardano-cli Vectors

    [Theory]
    [InlineData("addr1qx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgse35a3x", AddressType.Base, NetworkType.Mainnet, "key", "stake")]
    [InlineData("addr1z8phkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gten0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgs9yc0hh", AddressType.ScriptPaymentWithDelegation, NetworkType.Mainnet, "script", "stake")]
    [InlineData("addr1yx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzerkr0vd4msrxnuwnccdxlhdjar77j6lg0wypcc9uar5d2shs2z78ve", AddressType.PaymentWithScriptDelegation, NetworkType.Mainnet, "key", "script")]
    [InlineData("addr1x8phkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gt7r0vd4msrxnuwnccdxlhdjar77j6lg0wypcc9uar5d2shskhj42g", AddressType.ScriptPaymentWithScriptDelegation, NetworkType.Mainnet, "script", "script")]
    [InlineData("addr1vx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzers66hrl8", AddressType.EnterprisePayment, NetworkType.Mainnet, "key", null)]
    [InlineData(Cip19MainnetEnterpriseScript, AddressType.EnterpriseScriptPayment, NetworkType.Mainnet, "script", null)]
    [InlineData("stake1uyehkck0lajq8gr28t9uxnuvgcqrc6070x3k9r8048z8y5gh6ffgw", AddressType.Delegation, NetworkType.Mainnet, "", "stake")]
    [InlineData("stake178phkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtcccycj5", AddressType.ScriptDelegation, NetworkType.Mainnet, "", "script")]
    [InlineData("addr_test1qz2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgs68faae", AddressType.Base, NetworkType.Testnet, "key", "stake")]
    [InlineData("addr_test1zrphkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gten0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgsxj90mg", AddressType.ScriptPaymentWithDelegation, NetworkType.Testnet, "script", "stake")]
    [InlineData("addr_test1yz2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzerkr0vd4msrxnuwnccdxlhdjar77j6lg0wypcc9uar5d2shsf5r8qx", AddressType.PaymentWithScriptDelegation, NetworkType.Testnet, "key", "script")]
    [InlineData("addr_test1xrphkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gt7r0vd4msrxnuwnccdxlhdjar77j6lg0wypcc9uar5d2shs4p04xh", AddressType.ScriptPaymentWithScriptDelegation, NetworkType.Testnet, "script", "script")]
    [InlineData("addr_test1vz2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzerspjrlsz", AddressType.EnterprisePayment, NetworkType.Testnet, "key", null)]
    [InlineData("addr_test1wrphkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtcl6szpr", AddressType.EnterpriseScriptPayment, NetworkType.Testnet, "script", null)]
    [InlineData("stake_test1uqehkck0lajq8gr28t9uxnuvgcqrc6070x3k9r8048z8y5gssrtvn", AddressType.Delegation, NetworkType.Testnet, "", "stake")]
    [InlineData("stake_test17rphkx6acpnf78fuvxn0mkew3l0fd058hzquvz7w36x4gtcljw6kf", AddressType.ScriptDelegation, NetworkType.Testnet, "", "script")]
    public void Cip19Vector_ReadsAndBuilds(string vector, AddressType type, NetworkType network, string payment, string? stake)
    {
        Address built = new(network, type, Cip19Part(payment), stake is null ? null : Cip19Part(stake));
        Address parsed = Address.FromBech32(vector);

        Assert.Equal(vector, built.ToBech32());
        Assert.Equal(type, parsed.Type);
        Assert.Equal(network, parsed.Network);
        Assert.Equal(built.ToBytes(), parsed.ToBytes());
        Assert.Equal(vector, parsed.ToBech32());
    }

    [Theory]
    [InlineData(Cip19MainnetKeyPointer, AddressType.PaymentWithPointerDelegation, NetworkType.Mainnet, "key")]
    [InlineData(Cip19MainnetScriptPointer, AddressType.ScriptPaymentWithPointerDelegation, NetworkType.Mainnet, "script")]
    [InlineData(Cip19TestnetKeyPointer, AddressType.PaymentWithPointerDelegation, NetworkType.Testnet, "key")]
    [InlineData(Cip19TestnetScriptPointer, AddressType.ScriptPaymentWithPointerDelegation, NetworkType.Testnet, "script")]
    public void Cip19PointerVector_ReadsButDoesNotBuild(string vector, AddressType type, NetworkType network, string payment)
    {
        byte[] paymentPart = Cip19Part(payment);
        byte[] pointer = Cip19Part("pointer");
        _ = Assert.Throws<NotSupportedException>(() => new Address(network, type, paymentPart, pointer));

        Address parsed = Address.FromBech32(vector);
        Assert.Equal(type, parsed.Type);
        Assert.Equal(network, parsed.Network);
        Assert.Equal(paymentPart, parsed.GetPaymentKeyHash());
        Assert.Equal(pointer, parsed.ToBytes()[29..]);
        Assert.Null(parsed.GetStakeKeyHash());
        Assert.Equal(vector, parsed.ToBech32());
    }

    [Theory]
    [InlineData(Cip19MainnetKeyPointer)]
    [InlineData(Cip19MainnetScriptPointer)]
    [InlineData(Cip19TestnetKeyPointer)]
    [InlineData(Cip19TestnetScriptPointer)]
    public void Cip19PointerVector_HasNoPlutusAddress(string vector)
    {
        Address parsed = Address.FromBech32(vector);
        _ = Assert.Throws<NotSupportedException>(parsed.ToPlutusAddress);
    }

    // Issue #223: a payment-only script address (enterprise, type 7) is built from its script hash alone, with no stake part.
    [Fact]
    public void EnterpriseScriptAddress_NeedsNoStakePart()
    {
        // scripts/all as the ledger encodes a native script (conway.cddl:385-398 at cardano-ledger 95b369053f:
        // script_all = (1, [* native_script]), script_pubkey = (0, addr_keyhash)), hashed after the tag 0x00
        // (eras/allegra/impl/src/Cardano/Ledger/Allegra/Scripts.hs:360).
        byte[] script = [0x82, 0x01, 0x88, .. CliAllKeyHashes.SelectMany(hash => Convert.FromHexString("8200581c" + hash))];
        byte[] scriptHash = HashUtil.Blake2b224([0x00, .. script]);
        byte[] expected = [0x71, .. scriptHash];

        Assert.Equal(CliAllAddress, Address.FromScriptHash(NetworkType.Mainnet, scriptHash).ToBech32());
        Assert.Equal(CliAllAddress, new Address(NetworkType.Mainnet, AddressType.EnterpriseScriptPayment, scriptHash, null).ToBech32());
        Address parsed = Address.FromBech32(CliAllAddress);
        Assert.Equal(AddressType.EnterpriseScriptPayment, parsed.Type);
        Assert.Equal(expected, parsed.ToBytes());
        Assert.Null(parsed.GetStakeKeyHash());
    }

    #endregion

    #region Bech32 Prefix Tests

    [Fact]
    public void Mainnet_BaseAddress_HasAddrPrefix()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal("addr", address.GetPrefix());
        Assert.StartsWith("addr1", address.ToBech32(), StringComparison.Ordinal);
    }

    [Fact]
    public void Testnet_BaseAddress_HasAddrTestPrefix()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Testnet, AddressType.Base, paymentPub, stakePub);

        Assert.Equal("addr_test", address.GetPrefix());
        Assert.StartsWith("addr_test1", address.ToBech32(), StringComparison.Ordinal);
    }

    [Fact]
    public void Mainnet_StakeAddress_HasStakePrefix()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Mainnet);

        Assert.Equal("stake", stakeAddr.GetPrefix());
        Assert.StartsWith("stake1", stakeAddr.ToBech32(), StringComparison.Ordinal);
    }

    [Fact]
    public void Testnet_StakeAddress_HasStakeTestPrefix()
    {
        Address stakeAddr = CreateStakeAddress(NetworkType.Testnet);

        Assert.Equal("stake_test", stakeAddr.GetPrefix());
        Assert.StartsWith("stake_test1", stakeAddr.ToBech32(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkType.Preview)]
    [InlineData(NetworkType.Preprod)]
    public void TestnetVariant_StakeAddress_HasStakeTestPrefix(NetworkType networkType)
    {
        Address stakeAddr = CreateStakeAddress(networkType);

        Assert.StartsWith("stake_test1", stakeAddr.ToBech32(), StringComparison.Ordinal);
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
    public void GetAddressHeader_UnknownType_ThrowsArgumentOutOfRangeException() =>
        // Header byte 0x81 has type nibble 8 which is not a valid CIP-19 type
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Address.GetAddressHeader(0x81));

    [Fact]
    public void FromBech32_EmptyData_ThrowsFormatException()
    {
        FormatException error = Assert.Throws<FormatException>(() => Address.FromBech32(Bech32Util.Encode([], "addr")));
        Assert.Equal("The Bech32 string holds no address bytes.", error.Message);
    }

    [Fact]
    public void FromBytes_Empty_ThrowsArgumentException() =>
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes([]));

    [Theory]
    [InlineData("addr_test")]
    [InlineData("stake")]
    [InlineData("script")]
    public void FromBech32_RefusesAPrefixItsHeaderDoesNotGive(string prefix)
    {
        string vector = Bech32Util.Encode(Address.FromBech32(Cip19MainnetEnterpriseScript).ToBytes(), prefix);
        FormatException error = Assert.Throws<FormatException>(() => Address.FromBech32(vector));
        Assert.StartsWith($"The prefix {prefix} ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddressHeaderFromByte_ReadsAsGetAddressHeader()
    {
        Assert.Equal(Address.GetAddressHeader(0x71), AddressHeader.FromByte(0x71));
        _ = Assert.Throws<ArgumentOutOfRangeException>("headerByte", () => AddressHeader.FromByte(0x72));
        _ = Assert.Throws<ArgumentOutOfRangeException>("headerByte", () => AddressHeader.FromByte(0x81));
    }

    [Fact]
    public void ToHex_ReturnsValidHexString()
    {
        (PublicKey paymentPub, PublicKey stakePub) = CreateTestKeyPair();
        Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.Base, paymentPub, stakePub);

        string hex = address.ToHex();

        Assert.NotEmpty(hex);
        Assert.True(hex.All(c => "0123456789abcdef".Contains(c, StringComparison.Ordinal)));
    }

    #endregion
}
