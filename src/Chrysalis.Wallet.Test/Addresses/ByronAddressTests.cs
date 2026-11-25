using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using SimpleBase;

namespace Chrysalis.Wallet.Test.Addresses;

public class ByronAddressTests
{
    private const string SyntheticRootHex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c";
    private const string SyntheticRoot27Hex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b";
    private const string SyntheticRoot29Hex = SyntheticRootHex + "1d";

    // Test-only: Cardano Ledger 95b369053fd32422695a81fb7d5289d6dd35eee2 Golden/Address.hs:168-185 bootstrapAddr inner/crc.
    private const string BootstrapInnerHex = "83581c4bf3c2ee56bfef278d65f7388c46efa12a1069698e474f77adf0cf6aa000";
    private const string BootstrapCrcHex = "1ab4aad9a5";
    private const string BootstrapFullHex = "82d8185821" + BootstrapInnerHex + BootstrapCrcHex;

    // Test-only: Pallas v1.5.0-rc.2 daecef59a1979d89515524d9583cc28655af182c (identical at v0.34.0 2ab9eb45440d2ede38cf9e49026092a50c32c4e1;
    // Apache-2.0; licence in src/Chrysalis.Codec.Test/TestData/LICENSE-pallas), pallas-addresses/src/byron.rs:294-296
    // (sha256 e9687e7d41051ab9882a76830a21d4e24bda326b856e990f77904260c79ba665).
    [Theory]
    [InlineData(
        "Pallas-0-TestnetDaedalus",
        "37btjrVyb4KDXBNC4haBVPCrro8AQPHwvCMp3RFhhSVWwfFmZ6wwzSK6JK1hY6wHNmtrpTf1kdbva8TCneM2YsiXT7mrzT21EacHnPpz5YyUdj64na",
        83, NetworkType.Testnet, ByronAddressType.PublicKey, true, false,
        "7e9ee4a9527dea9091e2d580edd6716888c42f75d96276290f98fe0b",
        "0cdf39b531d1ac0963cbd183f63e43d895d16a9c567c95e1056e28bd",
        1097911063u, 0x53249B67u)]
    [InlineData(
        "Pallas-1-MainnetDaedalus",
        "DdzFFzCqrht7PQiAhzrn6rNNoADJieTWBt8KeK9BZdUsGyX9ooYD9NpMCTGjQoUKcHN47g8JMXhvKogsGpQHtiQ65fZwiypjrC6d3a4Q",
        76, NetworkType.Mainnet, ByronAddressType.PublicKey, true, false,
        "d30392160a29d76ebc0a7e14fef145dde2ae2c983aaf5f632b67b815",
        "ca3e553c9c63c534b988a9437ca105640410935a4e7380ca959381f0",
        null, 0x951A4859u)]
    [InlineData(
        "Pallas-2-MainnetIcarus",
        "Ae2tdPwUPEZLs4HtbuNey7tK4hTKrwNwYtGqp7bDfCy2WdR3P6735W5Yfpe",
        43, NetworkType.Mainnet, ByronAddressType.PublicKey, false, true,
        "f11939f42338d59e21baa08645ac1f0038d5ee969f99fe98f402fe79",
        null,
        null, 0xC9D64E5Bu)]
    public void FromBase58_PallasVectors_DecodesAllFieldsAndRoundTrips(
        string name,
        string base58,
        int expectedLength,
        NetworkType expectedNetwork,
        ByronAddressType expectedByronType,
        bool expectedDaedalus,
        bool expectedIcarus,
        string expectedRootHex,
        string? expectedDerivationHex,
        uint? expectedMagic,
        uint expectedCrc)
    {
        _ = name;
        Address address = Address.FromBase58(base58);
        byte[] bytes = address.ToBytes();

        Assert.Equal(expectedLength, bytes.Length);
        Assert.Equal(AddressType.Byron, address.Type);
        Assert.Equal(AddressEra.Byron, address.Era);
        Assert.Equal(expectedNetwork, address.Network);
        Assert.Equal(base58, address.ToBase58());
        Assert.Null(address.GetPaymentKeyHash());
        Assert.Null(address.GetStakeKeyHash());
        Assert.Null(address.GetPointer());

        ByronAddressInfo byron = Assert.IsType<ByronAddressInfo>(address.Byron);
        Assert.Equal(expectedByronType, byron.AddressType);
        Assert.Equal(expectedDaedalus, byron.IsDaedalus);
        Assert.Equal(expectedIcarus, byron.IsIcarus);
        Assert.Equal(expectedRootHex, Convert.ToHexStringLower(byron.Root.Span));
        Assert.Equal(expectedMagic, byron.NetworkMagic);
        Assert.Equal(expectedCrc, byron.Crc32);
        if (expectedDerivationHex is null)
        {
            Assert.Null(byron.DerivationPath);
        }
        else
        {
            _ = Assert.NotNull(byron.DerivationPath);
            Assert.Equal(expectedDerivationHex, Convert.ToHexStringLower(byron.DerivationPath.Value.Span));
        }

        Assert.Equal(base58, Address.FromBytes(bytes).ToBase58());
        Assert.Equal(base58, Address.FromHex(address.ToHex()).ToBase58());
    }

    // Test-only: Cardano Ledger 95b369053fd32422695a81fb7d5289d6dd35eee2 (Apache-2.0; licence and notice in
    // src/Chrysalis.Codec.Test/TestData/X7Attribution/{LICENSE,NOTICE-cardano-ledger}),
    // eras/shelley/test-suite/test/Test/Cardano/Ledger/Shelley/Serialisation/Golden/Address.hs:168-185 (bootstrapAddr)
    // and eras/byron/ledger/impl/golden/cbor/common/Address{0..4}.
    [Theory]
    [InlineData(
        "Ledger-bootstrapAddr",
        BootstrapFullHex,
        "Ae2tdPwUPEZ4PKL7S8HvYU94o9SA3ZcnTQV9ubMCcU7EhkZUvkUqDEouUyA",
        NetworkType.Mainnet, ByronAddressType.PublicKey, false, true,
        "4bf3c2ee56bfef278d65f7388c46efa12a1069698e474f77adf0cf6a",
        null, null, 0xB4AAD9A5u)]
    [InlineData(
        "Ledger-Address0-MainnetDaedalus",
        "82d818584683581cabeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7a101582258207447346a61306b466a4f38717a616932365a4d5055454a66457931356f78356b001a03f38741",
        null,
        NetworkType.Mainnet, ByronAddressType.PublicKey, true, false,
        "abeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7",
        "7447346a61306b466a4f38717a616932365a4d5055454a66457931356f78356b",
        null, 0x03F38741u)]
    [InlineData(
        "Ledger-Address1-MainnetIcarus",
        "82d818582183581caa5372095aaa680d19d4ca496983a145709c3be18b0d4c83cb7bdc5ea0001a32dc988e",
        null,
        NetworkType.Mainnet, ByronAddressType.PublicKey, false, true,
        "aa5372095aaa680d19d4ca496983a145709c3be18b0d4c83cb7bdc5e",
        null, null, 0x32DC988Eu)]
    [InlineData(
        "Ledger-Address2-MainnetRedeem",
        "82d818584683581cc6cbc43f74945040c7a5ed1b0584f7a081dbecfbbcf7a3603cfea23ca101582258203665744e6f756958657a4379456a4b63337447346a61306b466a4f38717a6169021a2b5c6320",
        null,
        NetworkType.Mainnet, ByronAddressType.Redeem, false, false,
        "c6cbc43f74945040c7a5ed1b0584f7a081dbecfbbcf7a3603cfea23c",
        "3665744e6f756958657a4379456a4b63337447346a61306b466a4f38717a6169",
        null, 0x2B5C6320u)]
    [InlineData(
        "Ledger-Address3-TestnetDaedalus",
        "82d818584d83581cbc89030e8b697017d0725b2c68c3623d77b6a061f46a3d4192604e80a20158225820744e6f756958657a4379456a4b63337447346a61306b466a4f38717a6169323602451a00982e0d001a159aca79",
        null,
        NetworkType.Testnet, ByronAddressType.PublicKey, true, false,
        "bc89030e8b697017d0725b2c68c3623d77b6a061f46a3d4192604e80",
        "744e6f756958657a4379456a4b63337447346a61306b466a4f38717a61693236",
        9973261u, 0x159ACA79u)]
    [InlineData(
        "Ledger-Address4-TestnetIcarus",
        "82d818582883581c823c60afca103d4981978d40b400bdaeb937ae91e2e36f4589d6938ba102451a00a98de7001acd66a453",
        null,
        NetworkType.Testnet, ByronAddressType.PublicKey, false, true,
        "823c60afca103d4981978d40b400bdaeb937ae91e2e36f4589d6938b",
        null, 11111911u, 0xCD66A453u)]
    public void FromHex_CardanoLedgerByronGoldens_DecodesBootstrapRedeemAndTestnet(
        string name,
        string hex,
        string? expectedBase58,
        NetworkType expectedNetwork,
        ByronAddressType expectedByronType,
        bool expectedDaedalus,
        bool expectedIcarus,
        string expectedRootHex,
        string? expectedDerivationHex,
        uint? expectedMagic,
        uint expectedCrc)
    {
        _ = name;
        Address address = Address.FromHex(hex);
        Assert.Equal(AddressType.Byron, address.Type);
        Assert.Equal(AddressEra.Byron, address.Era);
        Assert.Equal(expectedNetwork, address.Network);
        Assert.Equal(hex, address.ToHex());

        ByronAddressInfo byron = Assert.IsType<ByronAddressInfo>(address.Byron);
        Assert.Equal(expectedByronType, byron.AddressType);
        Assert.Equal(expectedDaedalus, byron.IsDaedalus);
        Assert.Equal(expectedIcarus, byron.IsIcarus);
        Assert.Equal(expectedRootHex, Convert.ToHexStringLower(byron.Root.Span));
        Assert.Equal(expectedMagic, byron.NetworkMagic);
        Assert.Equal(expectedCrc, byron.Crc32);
        if (expectedDerivationHex is null)
        {
            Assert.Null(byron.DerivationPath);
        }
        else
        {
            _ = Assert.NotNull(byron.DerivationPath);
            Assert.Equal(expectedDerivationHex, Convert.ToHexStringLower(byron.DerivationPath.Value.Span));
        }

        string encoded = address.ToBase58();
        if (expectedBase58 is not null)
        {
            Assert.Equal(expectedBase58, encoded);
        }

        Assert.Equal(hex, Address.FromBase58(encoded).ToHex());
    }

    [Fact]
    public void FromBytes_SyntheticEdgeCasesOwnershipAndLargeBoundary_DecodesCleanly()
    {
        // Pos_ScriptType1 (WA7-only per Decision 1(a); rejected by cardano-ledger AddrSpendingData.hs:101-113).
        byte[] scriptBytes = Convert.FromHexString("82d818582183581c" + SyntheticRootHex + "a0011a88621c4f");
        Address scriptAddr = Address.FromBytes(scriptBytes);
        ByronAddressInfo scriptInfo = Assert.IsType<ByronAddressInfo>(scriptAddr.Byron);
        Assert.Equal(ByronAddressType.Script, scriptInfo.AddressType);
        Assert.False(scriptInfo.IsDaedalus);
        Assert.False(scriptInfo.IsIcarus);
        Assert.Equal(0x88621C4Fu, scriptInfo.Crc32);

        // Pos_UnknownAttrKey3
        Address unknownAttr = Address.FromHex("82d818582783581c" + SyntheticRootHex + "a10344deadbeef001ac4cb733d");
        ByronAddressInfo unknownInfo = Assert.IsType<ByronAddressInfo>(unknownAttr.Byron);
        Assert.Equal(ByronAddressType.PublicKey, unknownInfo.AddressType);
        Assert.True(unknownInfo.IsIcarus);
        Assert.Null(unknownInfo.DerivationPath);
        Assert.Null(unknownInfo.NetworkMagic);
        Assert.Equal(0xC4CB733Du, unknownInfo.Crc32);

        // Pos_EmptyHdPayload ({1: h'40'} -> present empty derivation path, distinct from absent)
        Address emptyHd = Address.FromHex("82d818582483581c" + SyntheticRootHex + "a1014140001a7d0dcf0c");
        ByronAddressInfo emptyHdInfo = Assert.IsType<ByronAddressInfo>(emptyHd.Byron);
        _ = Assert.NotNull(emptyHdInfo.DerivationPath);
        Assert.Empty(emptyHdInfo.DerivationPath.Value.ToArray());
        Assert.True(emptyHdInfo.IsDaedalus);
        Assert.False(emptyHdInfo.IsIcarus);
        Assert.Equal(0x7D0DCF0Cu, emptyHdInfo.Crc32);

        // Pos_ExplicitMainnetMagic764824073 (presence of key 2 -> Testnet per Ledger AddrAttributes.hs)
        Address explicitMagic = Address.FromHex("82d818582883581c" + SyntheticRootHex + "a102451a2d964a09001ab01b2ce6");
        Assert.Equal(NetworkType.Testnet, explicitMagic.Network);
        ByronAddressInfo explicitInfo = Assert.IsType<ByronAddressInfo>(explicitMagic.Byron);
        Assert.Equal(764824073u, explicitInfo.NetworkMagic);
        Assert.Equal(0xB01B2CE6u, explicitInfo.Crc32);

        // Pos_SmallMagic1 (canonical 1-byte uint 0x01)
        Address smallMagic = Address.FromHex("82d818582483581c" + SyntheticRootHex + "a1024101001a86da1ea6");
        Assert.Equal(NetworkType.Testnet, smallMagic.Network);
        ByronAddressInfo smallInfo = Assert.IsType<ByronAddressInfo>(smallMagic.Byron);
        Assert.Equal(1u, smallInfo.NetworkMagic);
        Assert.Equal(0x86DA1EA6u, smallInfo.Crc32);

        // Boundary3653 (2,675 B outer envelope -> 3,653-char Base58 boundary from ByronAdapterTests.cs:48-50)
        byte[] boundaryOuter = new byte[2675];
        Convert.FromHexString("82d818590a6883581c").CopyTo(boundaryOuter, 0);
        Convert.FromHexString("a103590a43").CopyTo(boundaryOuter, 37);
        Convert.FromHexString("1abc2cc46c").CopyTo(boundaryOuter, 2670);
        Address boundary = Address.FromBytes(boundaryOuter);
        string boundaryBase58 = boundary.ToBase58();
        Assert.Equal(3653, boundaryBase58.Length);
        Assert.Equal(0xBC2CC46Cu, Assert.IsType<ByronAddressInfo>(boundary.Byron).Crc32);
        Assert.Equal(boundaryOuter, Address.FromBase58(boundaryBase58).ToBytes());

        // Byron ingress + egress defensive copy (including init-override guard on Byron is not null) & nonzero-offset span
        byte[] daedalusOriginal = Convert.FromHexString(
            "82d818584683581cabeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7a101582258207447346a61306b466a4f38717a616932365a4d5055454a66457931356f78356b001a03f38741");
        byte[] ingressCopy = (byte[])daedalusOriginal.Clone();
        Address fromArray = new(ingressCopy);
        string expectedBase58 = fromArray.ToBase58();
        Array.Fill(ingressCopy, (byte)0xFF);
        Assert.Equal(daedalusOriginal, fromArray.ToBytes());
        Assert.Equal(expectedBase58, fromArray.ToBase58());
        Assert.Equal("abeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7", Convert.ToHexStringLower(fromArray.Byron!.Root.Span));
        Assert.Equal("7447346a61306b466a4f38717a616932365a4d5055454a66457931356f78356b", Convert.ToHexStringLower(fromArray.Byron.DerivationPath!.Value.Span));

        byte[] egress1 = fromArray.ToBytes();
        byte[] egress2 = fromArray.ToBytes();
        Assert.NotSame(egress1, egress2);
        Array.Fill(egress1, (byte)0xEE);
        Assert.Equal(daedalusOriginal, fromArray.ToBytes());
        Assert.Equal(expectedBase58, fromArray.ToBase58());

        Address initOverridden = new(daedalusOriginal) { Type = AddressType.Base };
        byte[] overriddenBytes = initOverridden.ToBytes();
        Assert.NotSame(overriddenBytes, initOverridden.ToBytes());
        Array.Fill(overriddenBytes, (byte)0xDD);
        Assert.Equal(daedalusOriginal, initOverridden.ToBytes());
        Assert.Equal("abeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7", Convert.ToHexStringLower(initOverridden.Byron!.Root.Span));

        byte[] padded = [0xAA, 0xBB, .. daedalusOriginal, 0xCC];
        Address fromSlice = Address.FromBytes(padded.AsSpan(2, daedalusOriginal.Length));
        Array.Fill(padded, (byte)0x11);
        Assert.Equal(daedalusOriginal, fromSlice.ToBytes());
        Assert.Equal("abeaaa8f95c4529bb494e648c2e8e3522713f466a9dec1f1decea1b7", Convert.ToHexStringLower(fromSlice.Byron!.Root.Span));
        Assert.Equal("7447346a61306b466a4f38717a616932365a4d5055454a66457931356f78356b", Convert.ToHexStringLower(fromSlice.Byron.DerivationPath!.Value.Span));
    }

    // Opus R2 H1: pins Ledger's relaxed decodeListLenOf 3 non-minimal inner 3-element array head (98 03).
    [Fact]
    public void Pos_NonMinimalInnerArrayLen3_DecodesRelaxedLedgerInnerArrayHead()
    {
        const string hex = "82d81858229803581c" + SyntheticRootHex + "a0001a909125a0";
        byte[] raw = Convert.FromHexString(hex);

        Address fromHex = Address.FromHex(hex);
        Address fromBytes = Address.FromBytes(raw);
        Address fromBase58 = Address.FromBase58(fromHex.ToBase58());

        Assert.Equal(AddressType.Byron, fromHex.Type);
        Assert.Equal(AddressEra.Byron, fromHex.Era);
        Assert.Equal(NetworkType.Mainnet, fromHex.Network);
        Assert.Equal(raw, fromBytes.ToBytes());
        Assert.Equal(raw, fromBase58.ToBytes());

        ByronAddressInfo byron = Assert.IsType<ByronAddressInfo>(fromHex.Byron);
        Assert.Equal(ByronAddressType.PublicKey, byron.AddressType);
        Assert.Equal(SyntheticRootHex, Convert.ToHexStringLower(byron.Root.Span));
        Assert.Null(byron.DerivationPath);
        Assert.Null(byron.NetworkMagic);
        Assert.True(byron.IsIcarus);
        Assert.False(byron.IsDaedalus);
        Assert.Equal(0x909125A0u, byron.Crc32);
    }

    [Theory]
    [InlineData("Outer_TooShort5B", "82d8184000")]
    [InlineData("Outer_TruncatedAfterTag24", "82d818")]
    [InlineData("Outer_WrongTag25", "82d8195821" + BootstrapInnerHex + BootstrapCrcHex)]
    [InlineData("Outer_MissingTag24", "825821" + BootstrapInnerHex + BootstrapCrcHex)]
    [InlineData("Outer_DoubleTag24", "82d818d8185821" + BootstrapInnerHex + BootstrapCrcHex)]
    [InlineData("Outer_IndefinitePayloadBstr", "82d8185f5821" + BootstrapInnerHex + "ff" + BootstrapCrcHex)]
    [InlineData("Outer_MissingCrc", "82d8185821" + BootstrapInnerHex)]
    [InlineData("Outer_TaggedCrc", "82d8185821" + BootstrapInnerHex + "c0" + BootstrapCrcHex)]
    [InlineData("Outer_CrcExceedsUInt32", "82d8185821" + BootstrapInnerHex + "1b0000000100000000")]
    [InlineData("Outer_CrcMismatch", "82d8185821" + BootstrapInnerHex + "1ab4aad9a4")]
    [InlineData("Outer_TrailingByte", BootstrapFullHex + "00")]
    public void Decode_MalformedOuterEnvelope_ThrowsExpectedException(string name, string hex)
    {
        _ = name;
        byte[] raw = Convert.FromHexString(hex);
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes(raw));
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes(raw.AsSpan()));
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromHex(hex));
        _ = Assert.Throws<FormatException>(() => Address.FromBase58(Base58.Bitcoin.Encode(raw)));
    }

    [Theory]
    [InlineData("Neg_Inner_NotArray3", "82d818582082581c" + SyntheticRootHex + "a01a94f6dc55")]
    [InlineData("Neg_Inner_TruncatedAfterRoot", "82d818581f83581c" + SyntheticRootHex + "1a936a49de")]
    [InlineData("Neg_Inner_TruncatedAfterAttrs", "82d818582083581c" + SyntheticRootHex + "a01a652cd9ff")]
    [InlineData("Neg_Inner_ShortRoot27", "82d818582083581b" + SyntheticRoot27Hex + "a0001a4ea8329b")]
    [InlineData("Neg_Inner_LongRoot29", "82d818582283581d" + SyntheticRoot29Hex + "a0001abfa934d1")]
    [InlineData("Neg_Inner_IndefAttrsMap", "82d818582283581c" + SyntheticRootHex + "bfff001aa9acf9ee")]
    [InlineData("Neg_Inner_DupAttrKey", "82d818582783581c" + SyntheticRootHex + "a2024101024101001adc36e58d")]
    [InlineData("Neg_Inner_DescAttrKey", "82d818582783581c" + SyntheticRootHex + "a2024101014140001a27e13427")]
    [InlineData("Neg_Inner_Key1EmptyBstr", "82d818582383581c" + SyntheticRootHex + "a10140001a6afcbd78")]
    [InlineData("Neg_Inner_Key1BadInnerCbor", "82d818582483581c" + SyntheticRootHex + "a1014100001a8d748009")]
    [InlineData("Neg_Inner_Key1NonCanonLen", "82d818584383581c" + SyntheticRootHex + "a101581f59001c" + SyntheticRootHex + "001a2ebdbc40")]
    [InlineData("Neg_Inner_Key2EmptyBstr", "82d818582383581c" + SyntheticRootHex + "a10240001a68ba0321")]
    [InlineData("Neg_Inner_Key2NonCanonMagic", "82d818582583581c" + SyntheticRootHex + "a102421801001af568351e")]
    [InlineData("Neg_Inner_Key2TrailingByte", "82d818582583581c" + SyntheticRootHex + "a102420100001aff849ca0")]
    [InlineData("Neg_Inner_BadType3", "82d818582183581c" + SyntheticRootHex + "a0031a666c7d63")]
    [InlineData("Neg_Inner_NonCanonAddrType", "82d818582283581c" + SyntheticRootHex + "a018001aafe97888")]
    [InlineData("Neg_Inner_TrailingByte", "82d818582283581c" + SyntheticRootHex + "a000001a2df2e0d1")]
    public void Decode_MalformedInnerPayload_ThrowsExpectedException(string name, string hex)
    {
        _ = name;
        byte[] raw = Convert.FromHexString(hex);
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes(raw));
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes(raw.AsSpan()));
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromHex(hex));
        _ = Assert.Throws<FormatException>(() => Address.FromBase58(Base58.Bitcoin.Encode(raw)));
    }

    [Theory]
    [InlineData("Null", null, true)]
    [InlineData("Empty", "", false)]
    [InlineData("InvalidChar_0", "0", false)]
    [InlineData("InvalidChar_O", "O", false)]
    [InlineData("InvalidChar_I", "I", false)]
    [InlineData("InvalidChar_l", "l", false)]
    [InlineData("InvalidChar_Space", "Ae2 td", false)]
    [InlineData("InvalidChar_NonAscii", "Ae2étd", false)]
    [InlineData("ShelleyInBase58", "5QQQhQvU2jbXG7XMozcrUdw3Eig513uLNc14rqUB", false)]
    [InlineData("SingleZeroByte", "1", false)]
    [InlineData("OuterArrayLen3", "jsK75PTH2ertx6ypqvDFjcQAci5ML37pTiQbTu3ibDCsww8Vn5Yk8fAyGdR9", false)]
    public void FromBase58_InvalidStringsAndNonByronPayloads_ThrowsExpectedException(string name, string? input, bool isNull)
    {
        _ = name;
        if (isNull)
        {
            _ = Assert.Throws<ArgumentNullException>("base58Address", () => Address.FromBase58(input!));
        }
        else
        {
            _ = Assert.Throws<FormatException>(() => Address.FromBase58(input!));
        }
    }

    [Fact]
    public void ByronAndShelley_EncodingConventionAndUnsupportedOperations_Enforced()
    {
        byte[] bootstrapRaw = Convert.FromHexString(BootstrapFullHex);
        string byronAsBech32 = Bech32Util.Encode(bootstrapRaw, "addr");
        _ = Assert.Throws<FormatException>(() => Address.FromBech32(byronAsBech32));
        _ = Assert.Throws<FormatException>(() => new Address(byronAsBech32));

        Address byron = Address.FromBytes(bootstrapRaw);
        _ = Assert.Throws<NotSupportedException>(byron.ToBech32);
        _ = Assert.Throws<NotSupportedException>(byron.GetPrefix);
        _ = Assert.Throws<NotSupportedException>(byron.ToPlutusAddress);
        _ = Assert.Throws<NotSupportedException>(byron.ToPlutusStakeReference);
        Assert.Null(byron.GetPaymentKeyHash());
        Assert.Null(byron.GetStakeKeyHash());
        Assert.Null(byron.GetPointer());

        byte[] enterpriseRaw = [0x61, .. Convert.FromHexString(SyntheticRootHex)];
        Address shelley = Address.FromBytes(enterpriseRaw);
        Assert.Equal(AddressEra.Shelley, shelley.Era);
        Assert.Null(shelley.Byron);
        Assert.Null(shelley.GetPointer());
        _ = Assert.Throws<NotSupportedException>(shelley.ToBase58);
    }
}
