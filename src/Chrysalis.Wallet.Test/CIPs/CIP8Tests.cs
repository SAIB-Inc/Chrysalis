// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Buffers;
using System.Buffers.Text;
using System.Text;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Test;
using Chrysalis.Codec.Types;
using Chrysalis.Wallet.CIPs.CIP8.Builders;
using Chrysalis.Wallet.CIPs.CIP8.Extensions;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.CIPs.CIP8.Signers;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Wallet.Test.CIPs;

public class CIP8Tests
{
    // From CardanoSharp.Wallet's CIP-8 tests (CardanoSharp/cardanosharp-wallet `5089079d`, `CardanoSharp.Wallet.Test/CIPs/CIP8Tests.cs:121`; MIT).
    internal const string CardanoSharpSeed = "scout always message drill gorilla laptop electric decrease fly actor tuition merit clock flush end duck dance treat idle replace bulk total tool assist";

    // Test-only material, never a real key. Published CIP-8 vectors with pinned provenance:
    // - cardano-signer v1.35.0 (3cc4a7448d57f87dc01227f56ecbe04ea4c72a94, MIT, README.md:285, :289, :524-528; LICENSE-cardano-signer).
    // - cardano-verify-datasignature v1.0.11 (61d685846e77bf0eaa75c7c503ab0be313d57648, Apache-2.0, index.test.ts:71, :108;
    //   issue #9 2022-12-17 from an unrecorded Dec-2022 cardano-signer release; LICENSE-cardano-verify-datasignature).
    // - StricaHQ/cip08 v2.0.0 (6e90c07799d4484c8c66e9b0e8d04e8b7658e7e0, Apache-2.0, tests/main.spec.ts:25-27; LICENSE-stricahq-cip08).
    private const string CardanoSignerSeed = "c14ef0cc5e352446d6243976f51e8ffb2ae257f2a547c4fba170964a76501e7a";
    private const string CardanoSignerPublicKey = "9be513df12b3fabe7c1b8c3f9fab0968eb2168d5689bf981c2f7c35b11718b27";
    private const string CardanoSignerStakeAddressHex = "e0c13582aec9a44fcc6d984be003c5058c660e1d2ff1370fd8b49ba73f";
    private const string CvdAttachedHashedHex =
        "84582aa201276761646472657373581de0c13582aec9a44fcc6d984be003c5058c660e1d2ff1370fd8b49ba73f"
        + "a166686173686564f5581c40843181253eb1ff2258ab39c3463ec0edf5e713b73c5482c0ca798f"
        + "5840a4cdec07ba8c1184aa74d1c3516fc6602a35d2db847510cf98c102653c15c7664f136314f920150a081870aef77ed49780ca58873bd5d62e744b968a89435906";
    // Derived from cvd index.test.ts:71 by replacing slot 2 (581c<28-byte digest>) with CBOR null (f6).
    private const string DerivedDetachedHashedHex =
        "84582aa201276761646472657373581de0c13582aec9a44fcc6d984be003c5058c660e1d2ff1370fd8b49ba73f"
        + "a166686173686564f5f6"
        + "5840a4cdec07ba8c1184aa74d1c3516fc6602a35d2db847510cf98c102653c15c7664f136314f920150a081870aef77ed49780ca58873bd5d62e744b968a89435906";
    private const string CvdDetachedUnhashedHex =
        "84582aa201276761646472657373581de0c13582aec9a44fcc6d984be003c5058c660e1d2ff1370fd8b49ba73f"
        + "a166686173686564f4f6"
        + "58400a0dd23e867292a4c2eb692f63016e3f61294686f672065fcc377f665cff6b25c430619060b536073cfd2355ab6c6bcec9d7ecbfb588f7b0aa5967f1b8559300";
    private const string CardanoSignerDummyPublicKey = "755b017578b701dc9ddd4eaee67015b4ca8baf66293b7b1d204df426c0ceccb9";
    private const string CardanoSignerAttachedUnhashedHex =
        "84582aa201276761646472657373581d617863b5c43bdf0a06608abc82f0573a549714ff69166074dcdde393d8"
        + "a166686173686564f44b48656c6c6f20776f726c64"
        + "5840fc58155f0cee05bc00e7299af1df1f159ac82a46a055786b259657934eff346eec81349d4678ceabc79f213c66a2bdbfd4ea5d9ebdc630bee5ac9cce75cfc001";
    private const string EmurgoReferencePublicKey = "ea4a6c63e29c520abef5507b132ec5f9954776aebebe7b92421eea691446d22c";
    private const string EmurgoReferenceHashedHex =
        "845869a30127045820ea4a6c63e29c520abef5507b132ec5f9954776aebebe7b92421eea691446d22c"
        + "67616464726573735839006807b8aa9c7f462bf43125d9c071fd01d0720e8133fa9532ffd24c19db5e8ece0982acc4883de67c2e3411cc26bd56686a162074998c02bc"
        + "a166686173686564f5581c47c7d6f36f3ec09aa99785b789241921fd82b37005c764d5bbb7cf7a"
        + "584023eb07f7088e47eecb0af07c28a94b6125551698ae37a274631bdd3b801f8f819fbcc801cd23303f78ab329c5abcf15f206e7da89ad02384a4bae959ea5afe0f";
    private const string LegacyCip8Literal = "cms_hECgQEH__qfSERg";
    private static readonly byte[] HelloWorldBytes = "Hello world"u8.ToArray();
    private static readonly byte[] MehulPrajapatiBytes = "mehul prajapati"u8.ToArray();
    private static readonly byte[] WrongPayloadBytes = "Wrong payload"u8.ToArray();
    private static readonly byte[] MinimalSignature = [0xFF];

    /// <summary>
    /// Test vector from CardanoSharp - decode existing COSE_Sign1 message
    /// </summary>
    [Fact]
    public void DecodeCoseSign1Correctly()
    {
        // Test vector from CardanoSharp
        string coseSignMessageHex = "845869a30127045820674d11e432450118d70ea78673d5e31d5cc1aec63de0ff6284784876544be3406761646472657373583901d2eb831c6cad4aba700eb35f86966fbeff19d077954430e32ce65e8da79a3abe84f4ce817fad066acc1435be2ffc6bd7dce2ec1cc6cca6cba166686173686564f44568656c6c6f5840a3b5acd99df5f3b5e4449c5a116078e9c0fcfc126a4d4e2f6a9565f40b0c77474cafd89845e768fae3f6eec0df4575fcfe7094672c8c02169d744b415c617609";
        byte[] coseSignMessageBytes = Convert.FromHexString(coseSignMessageHex);

        // Deserialize the COSE_Sign1
        CoseSign1 coseSign1 = CborSerializer.Deserialize<CoseSign1>(coseSignMessageBytes);

        Assert.NotNull(coseSign1);
        Assert.NotNull(coseSign1.ProtectedHeaders);
        Assert.NotNull(coseSign1.Payload);
        Assert.NotNull(coseSign1.Signature);

        // The payload should be "hello"
        string payload = Encoding.UTF8.GetString(coseSign1.Payload);
        Assert.Equal("hello", payload);
    }

    /// <summary>
    /// Test simple message signing and verification
    /// </summary>
    [Fact]
    public void SignAndVerifyValidCoseSign1Message()
    {
        EdDsaCoseSigner signer = new();
        string message = "Hello Cardano!";

        // Generate a test mnemonic
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey("");

        // Derive keys using proper path parsing
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PrivateKey stakeKey = accountKey.Derive(RoleType.Staking).Derive(0);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);

        PublicKey stakePublicKey = stakeKey.GetPublicKey();
        PublicKey paymentPublicKey = paymentKey.GetPublicKey();

        // Create address using raw bytes constructor
        byte[] paymentKeyHash = HashUtil.Blake2b224(paymentPublicKey.Key);
        byte[] stakeKeyHash = HashUtil.Blake2b224(stakePublicKey.Key);
        Address address = new(NetworkType.Mainnet, AddressType.Base, paymentKeyHash, stakeKeyHash);

        // Build and sign message
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(message)
            .WithAddress(address)
            .Build(paymentKey);

        // Verify
        bool verified = signer.VerifyCoseSign1(coseSign1, paymentPublicKey);

        Assert.True(verified);
    }

    /// <summary>
    /// Test that verification fails with wrong key
    /// </summary>
    [Fact]
    public void VerificationFailsWithWrongKey()
    {
        EdDsaCoseSigner signer = new();
        string message = "Hello Cardano!";

        // Generate test mnemonic
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();

        // Derive two different payment keys
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PrivateKey paymentKey1 = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        PrivateKey paymentKey2 = accountKey.Derive(RoleType.ExternalChain).Derive(1);

        _ = paymentKey1.GetPublicKey();
        PublicKey publicKey2 = paymentKey2.GetPublicKey();

        // Sign with key1
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(message)
            .Build(paymentKey1);

        // Try to verify with key2 - should fail
        bool verified = signer.VerifyCoseSign1(coseSign1, publicKey2);

        Assert.False(verified);
    }

    /// <summary>
    /// Test CIP-8 format encoding/decoding
    /// </summary>
    [Fact]
    public void CIP8FormatRoundTrip()
    {
        string message = "Test message";
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);

        // Build message
        CoseSign1 original = new CoseSign1Builder()
            .WithPayload(message)
            .Build(paymentKey);

        // Convert to CIP-8 format
        string cip8String = original.ToCip8Format();

        // Should start with "cms_" and have checksum
        Assert.StartsWith("cms_", cip8String, StringComparison.Ordinal);
        Assert.Contains("_", cip8String[4..], StringComparison.Ordinal); // Should have second underscore for checksum

        // Parse back
        CoseSign1 parsed = CoseMessageExtensions.FromCip8Format(cip8String);

        // Verify it's the same
        _ = Assert.IsType<CoseSign1>(parsed);
        CoseSign1 parsedSign1 = parsed;

        Assert.Equal(original.ProtectedHeaders, parsedSign1.ProtectedHeaders);
        Assert.Equal(original.Payload, parsedSign1.Payload);
        Assert.Equal(original.Signature, parsedSign1.Signature);
    }

    /// <summary>
    /// Ensures CIP-8 parsing works when the checksum contains base64url underscores
    /// </summary>
    [Fact]
    public void CIP8FormatRoundTrip_WithUnderscoreInChecksum()
    {
        const int encodedChecksumLength = 6; // Base64Url length for the 4-byte FNV-1a checksum

        // Use deterministic headers/signature to keep the CBOR representation stable
        byte[] protectedHeaders = [];
        byte[] signature = [1, 2, 3, 4, 5, 6, 7, 8];
        CoseSign1? messageWithUnderscore = null;
        string? cip8WithUnderscore = null;

        for (int i = 0; i < 500; i++)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"payload-{i}");
            CoseSign1 candidate = new(protectedHeaders, CborMap.Create([]), payload, signature);
            string cip8 = candidate.ToCip8Format();
            string checksum = cip8[^encodedChecksumLength..];

            if (!checksum.Contains('_', StringComparison.Ordinal))
            {
                continue;
            }

            messageWithUnderscore = candidate;
            cip8WithUnderscore = cip8;
            break;
        }

        Assert.NotNull(messageWithUnderscore);
        Assert.NotNull(cip8WithUnderscore);

        CoseSign1 parsed = CoseMessageExtensions.FromCip8Format(cip8WithUnderscore);

        Assert.Equal(messageWithUnderscore.ProtectedHeaders, parsed.ProtectedHeaders);
        Assert.Equal(messageWithUnderscore.UnprotectedHeaders.Value, parsed.UnprotectedHeaders.Value);
        Assert.Equal(messageWithUnderscore.Payload, parsed.Payload);
        Assert.Equal(messageWithUnderscore.Signature, parsed.Signature);
    }

    /// <summary>
    /// Test hashed payload functionality
    /// </summary>
    [Fact]
    public void SignWithHashedPayload()
    {
        EdDsaCoseSigner signer = new();
        byte[] largePayload = new byte[1000]; // Large payload that would benefit from hashing
        Array.Fill(largePayload, (byte)42);

        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        PublicKey publicKey = paymentKey.GetPublicKey();

        // Build with hashed payload
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(largePayload)
            .HashPayload()
            .Build(paymentKey);

        // The payload in the message should be hashed (28 bytes for Blake2b224)
        Assert.Equal(28, coseSign1.Payload?.Length);

        // Verify attached digest (no double-hash) and raw plaintext correspondence
        Assert.True(signer.VerifyCoseSign1(coseSign1, publicKey));
        Assert.True(signer.VerifyCoseSign1(coseSign1, publicKey, payload: largePayload));
        Assert.False(signer.VerifyCoseSign1(coseSign1, publicKey, payload: WrongPayloadBytes));
        Assert.Equal(HashUtil.Blake2b224(largePayload), coseSign1.GetSigStructure(payload: largePayload).Payload);
        Assert.Equal(coseSign1.Payload, coseSign1.GetSigStructure().Payload);
    }

    /// <summary>
    /// Test external AAD functionality
    /// </summary>
    [Fact]
    public void SignWithExternalAad()
    {
        EdDsaCoseSigner signer = new();
        string message = "Transaction approval";
        byte[] externalAad = Encoding.UTF8.GetBytes("tx-id-12345");

        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        PublicKey publicKey = paymentKey.GetPublicKey();

        // Build with external AAD
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(message)
            .WithExternalAad(externalAad)
            .Build(paymentKey);

        // Verify with correct AAD
        bool verified = signer.VerifyCoseSign1(coseSign1, publicKey, externalAad: externalAad);
        Assert.True(verified);

        // Verify with wrong AAD should fail
        byte[] wrongAad = Encoding.UTF8.GetBytes("wrong-tx-id");
        bool verifiedWrong = signer.VerifyCoseSign1(coseSign1, publicKey, externalAad: wrongAad);
        Assert.False(verifiedWrong);

        // Verify without AAD should also fail
        bool verifiedNoAad = signer.VerifyCoseSign1(coseSign1, publicKey);
        Assert.False(verifiedNoAad);
    }

    /// <summary>
    /// Test detached payload functionality
    /// </summary>
    [Fact]
    public void SignWithDetachedPayload()
    {
        EdDsaCoseSigner signer = new();
        string message = "Secret message";
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        PublicKey publicKey = paymentKey.GetPublicKey();

        // Build with detached payload
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(message)
            .WithDetachedPayload()
            .Build(paymentKey);

        // Payload should be null in the message
        Assert.Null(coseSign1.Payload);

        // Verify requires providing the payload
        bool verified = signer.VerifyCoseSign1(coseSign1, publicKey, payload: messageBytes);
        Assert.True(verified);

        // Verify with wrong payload should fail
        byte[] wrongPayload = Encoding.UTF8.GetBytes("Wrong message");
        bool verifiedWrong = signer.VerifyCoseSign1(coseSign1, publicKey, payload: wrongPayload);
        Assert.False(verifiedWrong);

        // Verify without payload should fail
        _ = Assert.Throws<ArgumentException>(() =>
            signer.VerifyCoseSign1(coseSign1, publicKey));
    }

    /// <summary>
    /// Test compatibility with known test vector (from CardanoSharp)
    /// This uses a known seed phrase to ensure compatibility
    /// Recorded in CardanoSharp.Wallet's CIP-8 tests, with the same labels (same commit, lines 109–117).
    /// </summary>
    [Theory]
    [InlineData(
        "Lucid",
        "02708db4-fcd4-48d5-b228-52dd67a0dfd8",
        "845846a20127676164647265737358390183b612d7014a6fa718c252b578709adc8f78fb0c7c24d1bd1fa811ac5a30b33efe0365979f90ba3300b233ca81324c103904ea905546a9a7a166686173686564f4582430323730386462342d666364342d343864352d623232382d353264643637613064666438584043688accfc0488f661164b2124f5d061920a9e4aff84c8b25cce796bf15d6a6039035425ce296b00830c9c71e3cdc44e925db1304de46953424c5cf97b37820a")]
    [InlineData(
        "Eternl",
        "Hello Cardano!",
        "845846a20127676164647265737358390183b612d7014a6fa718c252b578709adc8f78fb0c7c24d1bd1fa811ac5a30b33efe0365979f90ba3300b233ca81324c103904ea905546a9a7a166686173686564f44e48656c6c6f2043617264616e6f21584037c3233f4e09dcca86747315f390bcc372f34b55372039533ccc9cb4dcbce5a9939f78ec119ab092cfceaebf88de4e43940704957aea42e5aa8c84908945a40f")]
    public void CompatibilityWithWebWallets(string wallet, string payload, string expectedSignature)
    {
        // Test parameters for different wallets
        _ = wallet; // Used to identify which wallet test case

        Mnemonic mnemonic = Mnemonic.Restore(CardanoSharpSeed, English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();

        // Standard Cardano wallet derivation
        PrivateKey accountKey = rootKey
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PrivateKey stakeKey = accountKey.Derive(RoleType.Staking).Derive(0);
        PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);

        PublicKey stakePublicKey = stakeKey.GetPublicKey();
        PublicKey paymentPublicKey = paymentKey.GetPublicKey();

        // Create mainnet base address
        Address address = Address.FromPublicKeys(
            NetworkType.Mainnet,
            AddressType.Base,
            paymentPublicKey,
            stakePublicKey
        );

        // Build the message
        CoseSign1 coseSign1 = new CoseSign1Builder()
            .WithPayload(payload)
            .WithAddress(address)
            .Build(paymentKey);

        // Serialize to CBOR
        string signatureHex = Convert.ToHexStringLower(coseSign1.ToCbor());

        Assert.Equal(expectedSignature, signatureHex);

        // Verify it's valid
        EdDsaCoseSigner signer = new();
        bool verified = signer.VerifyCoseSign1(coseSign1, paymentPublicKey);
        Assert.True(verified);
    }

    [Fact]
    public void CardanoSignerHashedVector_SignsByteForByte()
    {
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        Address address = Address.FromHex(CardanoSignerStakeAddressHex);

        CoseSign1 built = new CoseSign1Builder()
            .WithPayload(HelloWorldBytes)
            .WithAddress(address)
            .HashPayload()
            .Build(signingKey);

        Assert.Equal(CvdAttachedHashedHex, Convert.ToHexStringLower(built.ToCbor()));
        Assert.Equal(CardanoSignerPublicKey, Convert.ToHexStringLower(signingKey.GetPublicKey().Key));
    }

    [Fact]
    public void CardanoSignerHashedVector_VerifiesAttachedAndRawPayload()
    {
        CoseSign1 golden = CoseSign1.FromCbor(Convert.FromHexString(CvdAttachedHashedHex));
        PublicKey publicKey = new(Convert.FromHexString(CardanoSignerPublicKey), new byte[32]);
        EdDsaCoseSigner signer = new();

        Assert.True(signer.VerifyCoseSign1(golden, publicKey));
        Assert.True(signer.VerifyCoseSign1(golden, publicKey, payload: HelloWorldBytes));
        Assert.False(signer.VerifyCoseSign1(golden, publicKey, payload: WrongPayloadBytes));
        Assert.Equal(HashUtil.Blake2b224(HelloWorldBytes), golden.GetSigStructure(payload: HelloWorldBytes).Payload);
    }

    [Fact]
    public void CardanoSignerDerivedDetachedHashedVector_VerifiesWithRawPayload()
    {
        CoseSign1 detached = CoseSign1.FromCbor(Convert.FromHexString(DerivedDetachedHashedHex));
        PublicKey publicKey = new(Convert.FromHexString(CardanoSignerPublicKey), new byte[32]);
        EdDsaCoseSigner signer = new();

        Assert.Null(detached.Payload);
        Assert.True(signer.VerifyCoseSign1(detached, publicKey, payload: HelloWorldBytes));
        Assert.False(signer.VerifyCoseSign1(detached, publicKey, payload: WrongPayloadBytes));
        Assert.True(publicKey.Verify(detached.GetSigStructure(payload: HelloWorldBytes).ToCbor(), detached.Signature));
        _ = Assert.Throws<ArgumentException>("payload", () => signer.VerifyCoseSign1(detached, publicKey));
    }

    [Fact]
    public void CardanoSignerDetachedAndAttachedUnhashedVectors_Verify()
    {
        EdDsaCoseSigner signer = new();
        CoseSign1 detached = CoseSign1.FromCbor(Convert.FromHexString(CvdDetachedUnhashedHex));
        PublicKey stakeKey = new(Convert.FromHexString(CardanoSignerPublicKey), new byte[32]);

        Assert.True(signer.VerifyCoseSign1(detached, stakeKey, payload: HelloWorldBytes));
        Assert.False(signer.VerifyCoseSign1(detached, stakeKey, payload: WrongPayloadBytes));

        CoseSign1 attached = CoseSign1.FromCbor(Convert.FromHexString(CardanoSignerAttachedUnhashedHex));
        PublicKey dummyKey = new(Convert.FromHexString(CardanoSignerDummyPublicKey), new byte[32]);

        Assert.True(signer.VerifyCoseSign1(attached, dummyKey));
        Assert.True(signer.VerifyCoseSign1(attached, dummyKey, payload: HelloWorldBytes));
        Assert.False(signer.VerifyCoseSign1(attached, dummyKey, payload: WrongPayloadBytes));
    }

    [Fact]
    public void EmurgoReferenceHashedVector_VerifiesAttachedAndRawPayload()
    {
        CoseSign1 emurgo = CoseSign1.FromCbor(Convert.FromHexString(EmurgoReferenceHashedHex));
        PublicKey publicKey = new(Convert.FromHexString(EmurgoReferencePublicKey), new byte[32]);
        EdDsaCoseSigner signer = new();

        Assert.True(signer.VerifyCoseSign1(emurgo, publicKey));
        Assert.True(signer.VerifyCoseSign1(emurgo, publicKey, payload: MehulPrajapatiBytes));
        Assert.False(signer.VerifyCoseSign1(emurgo, publicKey, payload: WrongPayloadBytes));
    }

    [Fact]
    public void HashedPayload_EmptyAndExternalAad_DetachedVerifies()
    {
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        PublicKey publicKey = signingKey.GetPublicKey();
        byte[] externalAad = "aad-ctx"u8.ToArray();
        byte[] emptyPayload = [];
        EdDsaCoseSigner signer = new();

        CoseSign1 detached = new CoseSign1Builder()
            .WithPayload(emptyPayload)
            .WithExternalAad(externalAad)
            .WithDetachedPayload()
            .HashPayload()
            .Build(signingKey);

        Assert.Null(detached.Payload);
        Assert.Equal(HashUtil.Blake2b224(emptyPayload), detached.GetSigStructure(externalAad, emptyPayload).Payload);
        Assert.True(signer.VerifyCoseSign1(detached, publicKey, externalAad, emptyPayload));
        Assert.False(signer.VerifyCoseSign1(detached, publicKey, externalAad: "other"u8.ToArray(), payload: emptyPayload));
        Assert.False(signer.VerifyCoseSign1(detached, publicKey, externalAad: externalAad, payload: WrongPayloadBytes));
        _ = Assert.Throws<ArgumentException>("payload", () => detached.GetSigStructure(externalAad));
        _ = Assert.Throws<ArgumentException>("payload", () => signer.VerifyCoseSign1(detached, publicKey, externalAad));
    }

    [Theory]
    [InlineData("non-bool-int", "a16668617368656401")]
    [InlineData("non-bool-string", "a1666861736865646474727565")]
    [InlineData("non-bool-null", "a166686173686564f6")]
    [InlineData("non-bool-tagged", "a166686173686564c0f5")]
    [InlineData("duplicate-hashed", "a266686173686564f566686173686564f4")]
    public void MalformedHashedHeader_GetSigStructureThrowsAndVerifyReturnsFalse(string caseName, string unprotectedMapHex)
    {
        _ = caseName;
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        PublicKey publicKey = signingKey.GetPublicKey();
        CoseSign1 signed = new CoseSign1Builder().WithPayload(HelloWorldBytes).Build(signingKey);
        CoseSign1 malformed = BuildWireCoseSign1(signed.ProtectedHeaders, unprotectedMapHex, signed.Payload, signed.Signature);
        EdDsaCoseSigner signer = new();

        Assert.Multiple(
            () => _ = Assert.Throws<FormatException>(() => malformed.GetSigStructure()),
            () => Assert.False(signer.VerifyCoseSign1(malformed, publicKey)));
    }

    [Fact]
    public void FlippedHashedFlagOnNon28BytePayload_GetSigStructureThrowsAndVerifyReturnsFalse()
    {
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        PublicKey publicKey = signingKey.GetPublicKey();
        CoseSign1 signed = new CoseSign1Builder().WithPayload(HelloWorldBytes).Build(signingKey);
        CoseSign1 tampered = BuildWireCoseSign1(signed.ProtectedHeaders, "a166686173686564f5", signed.Payload, signed.Signature);
        EdDsaCoseSigner signer = new();

        Assert.Multiple(
            () => _ = Assert.Throws<FormatException>(() => tampered.GetSigStructure()),
            () => Assert.False(signer.VerifyCoseSign1(tampered, publicKey)));
    }

    [Theory]
    [InlineData("unhashed-tampered-attachment", false)]
    [InlineData("hashed-tampered-attachment", true)]
    public void AttachedPayloadMismatch_GetSigStructureThrowsAndVerifyReturnsFalse(string caseName, bool hashPayload)
    {
        _ = caseName;
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        PublicKey publicKey = signingKey.GetPublicKey();
        CoseSign1Builder builder = new CoseSign1Builder().WithPayload(HelloWorldBytes);
        if (hashPayload)
        {
            _ = builder.HashPayload();
        }

        CoseSign1 signed = builder.Build(signingKey);
        byte[] tamperedAttachment = hashPayload ? HashUtil.Blake2b224(WrongPayloadBytes) : WrongPayloadBytes;
        string unprotectedHex = hashPayload ? "a166686173686564f5" : "a166686173686564f4";
        CoseSign1 tampered = BuildWireCoseSign1(signed.ProtectedHeaders, unprotectedHex, tamperedAttachment, signed.Signature);
        EdDsaCoseSigner signer = new();

        Assert.Multiple(
            () => _ = Assert.Throws<FormatException>(() => tampered.GetSigStructure(payload: HelloWorldBytes)),
            () => Assert.False(signer.VerifyCoseSign1(tampered, publicKey, payload: HelloWorldBytes)));
    }

    [Theory]
    [InlineData(
        "definite-tagged-and-null-with-hashed-true",
        "a4c066686173686564f4627473c06a323032362d30312d3031646e6f7465f666686173686564f5",
        true)]
    [InlineData(
        "indefinite-tagged-with-hashed-true",
        "bf044101627473c06a323032362d30312d303166686173686564f5ff",
        true)]
    [InlineData("missing-hashed-defaults-false", "a0", false)]
    public void ExtraAnnotationsInUnprotectedHeaders_HonorsHashedAndSkipsUnknownValues(
        string caseName,
        string unprotectedMapHex,
        bool hashPayload)
    {
        _ = caseName;
        PrivateKey signingKey = new(Convert.FromHexString(CardanoSignerSeed), new byte[32]);
        PublicKey publicKey = signingKey.GetPublicKey();
        CoseSign1Builder builder = new CoseSign1Builder().WithPayload(HelloWorldBytes).WithDetachedPayload();
        if (hashPayload)
        {
            _ = builder.HashPayload();
        }

        CoseSign1 signed = builder.Build(signingKey);
        CoseSign1 wire = BuildWireCoseSign1(signed.ProtectedHeaders, unprotectedMapHex, null, signed.Signature);
        EdDsaCoseSigner signer = new();

        Assert.True(signer.VerifyCoseSign1(wire, publicKey, payload: HelloWorldBytes));
        if (!hashPayload)
        {
            CoseSign1 withDefaultMap = new(signed.ProtectedHeaders, default, null, signed.Signature);
            Assert.True(signer.VerifyCoseSign1(withDefaultMap, publicKey, payload: HelloWorldBytes));
        }
    }

    [Fact]
    public void Cip8Format_LegacyLiteral_RoundTripsIdentically()
    {
        CoseSign1 minimal = new([], CborMap.Create([]), [], MinimalSignature);

        Assert.Equal(LegacyCip8Literal, minimal.ToCip8Format());

        CoseSign1 parsed = CoseMessageExtensions.FromCip8Format(LegacyCip8Literal);

        Assert.Empty(parsed.ProtectedHeaders);
        Assert.Empty(parsed.UnprotectedHeaders.Value);
        Assert.NotNull(parsed.Payload);
        Assert.Empty(parsed.Payload);
        Assert.Equal(MinimalSignature, parsed.Signature);
        Assert.Equal(LegacyCip8Literal, parsed.ToCip8Format());
    }

    [Theory]
    [InlineData("small", 16)]
    [InlineData("large-1kb", 1024)]
    public void ToCip8Format_SpanBase64Url_AvoidsExtraAllocations(string caseName, int payloadLength)
    {
        byte[] payload = new byte[payloadLength];
        Array.Fill(payload, (byte)0x5A);
        CoseSign1 message = new([], CborMap.Create([]), payload, new byte[64]);
        int expectedLength = message.ToCip8Format().Length;

        long cborAlloc = AllocationAssert.Measure(
            () => _ = message.ToCbor(),
            () => _ = message.ToCbor());
        long stringAlloc = AllocationAssert.Measure(
            () => _ = string.Create(expectedLength, 0, static (_, _) => { }),
            () => _ = string.Create(expectedLength, 0, static (_, _) => { }));
        long actualAlloc = AllocationAssert.Measure(
            () => _ = message.ToCip8Format(),
            () => _ = message.ToCip8Format());

        Assert.True(
            actualAlloc <= cborAlloc + stringAlloc,
            $"ToCip8Format ({caseName}) allocated {actualAlloc} B, expected <= {cborAlloc + stringAlloc} B.");
    }

    [Theory]
    [InlineData("small", 16)]
    [InlineData("large-1kb", 1024)]
    public void FromCip8Format_SpanBase64Url_AvoidsExtraAllocations(string caseName, int payloadLength)
    {
        byte[] payload = new byte[payloadLength];
        Array.Fill(payload, (byte)0x5A);
        CoseSign1 message = new([], CborMap.Create([]), payload, new byte[64]);
        byte[] cborBytes = message.ToCbor();
        string cip8 = message.ToCip8Format();

        void Baseline()
        {
            byte[] buffer = new byte[cborBytes.Length];
            cborBytes.AsSpan().CopyTo(buffer);
            _ = CborSerializer.Deserialize<CoseSign1>(buffer);
        }

        long expectedAlloc = AllocationAssert.Measure(Baseline, Baseline);
        long actualAlloc = AllocationAssert.Measure(
            () => _ = CoseMessageExtensions.FromCip8Format(cip8),
            () => _ = CoseMessageExtensions.FromCip8Format(cip8));

        Assert.True(
            actualAlloc <= expectedAlloc,
            $"FromCip8Format ({caseName}) allocated {actualAlloc} B, expected <= {expectedAlloc} B.");
    }

    [Theory]
    [InlineData("plus", "cms_", "hECgQEH+")]
    [InlineData("slash", "cms_", "hECgQEH/")]
    [InlineData("whitespace", "cms_", "hECg    QEH_")]
    [InlineData("equals-padding", "cms_", "hECgQEIBAA==")]
    [InlineData("non-ascii-prefix", "cm\u00e9_", "hECgQEH_")]
    [InlineData("percent-padding", "cms_", "hECgQEIBAA%%")]
    public void FromCip8Format_NonCanonicalBase64UrlWithValidChecksum_ThrowsFormatException(
        string caseName,
        string prefix,
        string data)
    {
        _ = caseName;
        string input = MakeCip8WithValidUtf8Checksum(prefix, data);

        _ = Assert.Throws<FormatException>(() => CoseMessageExtensions.FromCip8Format(input));
    }

    [Theory]
    [InlineData("plus-char-stale-checksum", "hECgQEH-", "hECgQEH+")]
    [InlineData("nonzero-trailing-bits-stale-checksum", "hECgQEIBAA", "hECgQEIBAB")]
    public void FromCip8Format_NonCanonicalInputWithStaleChecksum_ThrowsFormatExceptionBeforeChecksum(
        string caseName,
        string canonicalData,
        string mutatedData)
    {
        _ = caseName;
        string valid = MakeCip8WithValidUtf8Checksum("cms_", canonicalData);
        string stale = valid.Replace(canonicalData, mutatedData, StringComparison.Ordinal);

        _ = Assert.Throws<FormatException>(() => CoseMessageExtensions.FromCip8Format(stale));
    }

    [Fact]
    public void FromCip8Format_InvalidChecksum_ThrowsInvalidOperationException()
    {
        string corrupted = "cms_hECgQEH__rfSERg";

        _ = Assert.Throws<InvalidOperationException>(() => CoseMessageExtensions.FromCip8Format(corrupted));
    }

    private static CoseSign1 BuildWireCoseSign1(
        byte[] protectedHeaders,
        string unprotectedMapHex,
        byte[]? payload,
        byte[] signature)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        writer.WriteBeginArray(4);
        writer.WriteByteString(protectedHeaders);
        buffer.Write(Convert.FromHexString(unprotectedMapHex));
        if (payload is null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteByteString(payload);
        }

        writer.WriteByteString(signature);
        writer.WriteEndArray(4);
        return CoseSign1.FromCbor(buffer.WrittenSpan.ToArray());
    }

    private static string MakeCip8WithValidUtf8Checksum(string prefix, string data)
    {
        uint hash = 0x811c9dc5;
        foreach (byte b in Encoding.UTF8.GetBytes($"{prefix}{data}"))
        {
            hash ^= b;
            hash *= 0x01000193;
        }

        string checksum = Base64Url.EncodeToString(BitConverter.GetBytes(hash));
        return $"{prefix}{data}_{checksum}";
    }
}
