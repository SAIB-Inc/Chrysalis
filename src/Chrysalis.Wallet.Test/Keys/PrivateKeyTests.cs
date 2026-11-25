using Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Test;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Crypto;
using Chrysalis.Crypto.Test;
using Chrysalis.Tx.Extensions;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Keys;

public class PrivateKeyTests
{
    // Test-only material, never a real key: cardano-cli's own golden test hprop_golden_shelley_transaction_sign
    // (cardano-cli/test/cardano-cli-golden/Test/Golden/Shelley/Transaction/Sign.hs:12-45) at tag cardano-cli-11.7.0.0,
    // commit 24c05829e854f9b2f56dcb9422bff5ff3ff60f77, https://github.com/IntersectMBO/cardano-cli, Apache-2.0
    // (licence and notice in src/Chrysalis.IntegrationTests/Golden/cardano-cli-11.7.0.0/). Under
    // cardano-cli/test/cardano-cli-golden/files/, with sha256:
    //   input/shelley/keys/payment_keys/signing_key       0294990f2ab4f91f0a155939d5262d80653dff2c43aa08ab7f020d580239c7f9
    //   input/shelley/keys/payment_keys/verification_key  03dd29805ca3176f6f3c0613995ecea56b30af4be088653d219a12e196a02a62
    //   input/shelley/tx/txbody                           c3d463db676630aa21873570c151b91811fb8a16f64b709dc3874f093449605f
    //   golden/shelley/transaction-sign-1.json            35c432a5c7e6802dd5568c6957e7eddd9722f914d64e034660034eecece04a1c
    // CliSeed is the signing key's cborHex after its 5820 head, CliVerificationKey the verification key's, CliBody the
    // body inside txbody, and CliSignature the witness signature in transaction-sign-1.json.
    internal const string CliSeed = "8547281266bc5467a10f2f50ddf10e2d0c05ebf780785ebee40c1869b08c1c35";
    internal const string CliVerificationKey = "8dc60533b5dfa60a530955a696323a2ef4f14e8bc95a8f84cf6c441fea423427";
    private const string CliBody = "a4008182582091999ea21177b33ebe6b8690724a0c026d410a11ad7521caa350abdafa5394c300018182581d615dbe1e2117641f8d618034b801a870ca731ce758c3bedd5c7e4429c11a05f5e100021a000f4240031a0007a120";
    private const string CliSignature = "9f69aa4bf41acf78deaffb002c4f36157f3bad7c434a19b1ffe7e7df5f98f629e58503a762e83c1f60e54c9eb7eef9885b16c2b42ce1336914f2df9aa63b1407";

    private static PrivateKey CreateTestRootKey()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            TestVectors.Bip39ZeroEntropy12,
            English.Words);
        return mnemonic.GetRootKey();
    }

    [Fact]
    public void GetRootKey_ProducesValidKey()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();

        Assert.Equal(64, rootKey.Key.Length);
    }

    [Fact]
    public void GetRootKey_WithPassword_ProducesDifferentKey()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);

        PrivateKey keyWithoutPassword = mnemonic.GetRootKey();
        PrivateKey keyWithPassword = mnemonic.GetRootKey("test-password");

        Assert.False(keyWithoutPassword.Key.SequenceEqual(keyWithPassword.Key));
    }

    [Fact]
    public void GetRootKey_SameMnemonic_ProducesSameKey()
    {
        string phrase = TestVectors.Bip39ZeroEntropy12;

        Mnemonic mnemonic1 = Mnemonic.Restore(phrase, English.Words);
        Mnemonic mnemonic2 = Mnemonic.Restore(phrase, English.Words);

        PrivateKey key1 = mnemonic1.GetRootKey();
        PrivateKey key2 = mnemonic2.GetRootKey();

        Assert.True(key1.Key.SequenceEqual(key2.Key));
        Assert.True(key1.Chaincode.SequenceEqual(key2.Chaincode));
    }

    [Fact]
    public void Derive_SoftDerivation_ProducesValidChildKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PrivateKey childKey = rootKey.Derive(0, DerivationType.SOFT);

        Assert.Equal(64, childKey.Key.Length);
        Assert.False(rootKey.Key.SequenceEqual(childKey.Key));
    }

    [Fact]
    public void Derive_HardDerivation_ProducesValidChildKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PrivateKey childKey = rootKey.Derive(0, DerivationType.HARD);

        Assert.Equal(64, childKey.Key.Length);
        Assert.False(rootKey.Key.SequenceEqual(childKey.Key));
    }

    [Fact]
    public void Derive_SameIndex_ProducesSameChildKey()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();

        PrivateKey child1 = rootKey1.Derive(0, DerivationType.HARD);
        PrivateKey child2 = rootKey2.Derive(0, DerivationType.HARD);

        Assert.True(child1.Key.SequenceEqual(child2.Key));
    }

    [Fact]
    public void Derive_DifferentIndex_ProducesDifferentKey()
    {
        PrivateKey rootKey = CreateTestRootKey();

        PrivateKey child0 = rootKey.Derive(0, DerivationType.HARD);
        PrivateKey child1 = rootKey.Derive(1, DerivationType.HARD);

        Assert.False(child0.Key.SequenceEqual(child1.Key));
    }

    [Fact]
    public void Derive_CardanoPath_ProducesExpectedStructure()
    {
        PrivateKey rootKey = CreateTestRootKey();

        // Derive m/1852'/1815'/0'/0/0 (typical Cardano payment key path)
        PrivateKey purposeKey = rootKey.Derive(PurposeType.Shelley, DerivationType.HARD);
        PrivateKey coinKey = purposeKey.Derive(CoinType.Ada, DerivationType.HARD);
        PrivateKey accountKey = coinKey.Derive(0, DerivationType.HARD);
        PrivateKey roleKey = accountKey.Derive(RoleType.ExternalChain);
        PrivateKey addressKey = roleKey.Derive(0);

        Assert.Equal(64, addressKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_ReturnsValidPublicKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PublicKey publicKey = rootKey.GetPublicKey();

        Assert.NotNull(publicKey);
        Assert.Equal(32, publicKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_SamePrivateKey_ProducesSamePublicKey()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();

        PublicKey publicKey1 = rootKey1.GetPublicKey();
        PublicKey publicKey2 = rootKey2.GetPublicKey();

        Assert.Equal(publicKey1.Key, publicKey2.Key);
    }

    [Fact]
    public void Sign_ProducesValidSignature()
    {
        PrivateKey rootKey = CreateTestRootKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();

        byte[] signature = rootKey.Sign(message);

        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public void Sign_SameMessage_ProducesSameSignature()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();

        byte[] signature1 = rootKey1.Sign(message);
        byte[] signature2 = rootKey2.Sign(message);

        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_DifferentMessage_ProducesDifferentSignature()
    {
        PrivateKey rootKey = CreateTestRootKey();
        byte[] message1 = "Hello, Cardano!"u8.ToArray();
        byte[] message2 = "Goodbye, Cardano!"u8.ToArray();

        byte[] signature1 = rootKey.Sign(message1);
        byte[] signature2 = rootKey.Sign(message2);

        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void Equals_SameKey_ReturnsTrue()
    {
        PrivateKey key1 = CreateTestRootKey();
        PrivateKey key2 = CreateTestRootKey();

        Assert.True(key1.Equals(key2));
    }

    [Fact]
    public void Equals_DifferentKey_ReturnsFalse()
    {
        Mnemonic mnemonic1 = Mnemonic.Generate(English.Words, 12);
        Mnemonic mnemonic2 = Mnemonic.Generate(English.Words, 12);

        PrivateKey key1 = mnemonic1.GetRootKey();
        PrivateKey key2 = mnemonic2.GetRootKey();

        Assert.False(key1.Equals(key2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        PrivateKey key = CreateTestRootKey();

        Assert.False(key.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameKey_ReturnsSameHash()
    {
        PrivateKey key1 = CreateTestRootKey();
        PrivateKey key2 = CreateTestRootKey();

        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_MakesNoCopyOfTheKey()
    {
        PrivateKey key = CreateTestRootKey();
        void Hash() => _ = key.GetHashCode();

        Assert.Equal(0, AllocationAssert.Measure(Hash, Hash));
    }

    [Fact]
    public void SeedKey_HasTheRfc8032PublicKeyAndSignature()
    {
        foreach (Ed25519TestVector vector in Ed25519TestVectors.TestCases)
        {
            PrivateKey key = new(vector.Seed, new byte[32]);
            byte[] publicKey = key.GetPublicKey().Key;
            byte[] signature = key.Sign(vector.Message);

            Assert.Equal(vector.PublicKey, publicKey);
            Assert.Equal(vector.Signature, signature);
            Assert.True(key.GetPublicKey().Verify(vector.Message, signature));
        }
    }

    [Fact]
    public void CliSeedKey_HasTheCliVerificationKey()
    {
        PrivateKey key = new(Convert.FromHexString(CliSeed), new byte[32]);

        Assert.Equal(CliVerificationKey, Convert.ToHexStringLower(key.GetPublicKey().Key));
    }

    [Fact]
    public void SeedKey_WitnessEqualsTheCliWitness()
    {
        PrivateKey key = new(Convert.FromHexString(CliSeed), new byte[32]);

        VKeyWitness witness = Assert.Single(Witnesses(CliTransaction().Sign(key)));

        Assert.Equal(CliVerificationKey, Convert.ToHexStringLower(witness.VKey.Span));
        Assert.Equal(CliSignature, Convert.ToHexStringLower(witness.Signature.Span));
        AssertVerifiesTheBody(witness);
    }

    [Fact]
    public void SeedAndExtendedKeys_WitnessesVerifyTheBody()
    {
        PrivateKey seedKey = new(Convert.FromHexString(CliSeed), new byte[32]);
        PrivateKey extendedKey = CreateTestRootKey().DeriveCardanoAccountKey().DerivePaymentKey();

        VKeyWitness[] witnesses = Witnesses(CliTransaction().Sign(seedKey, extendedKey));

        Assert.Equal(2, witnesses.Length);
        Assert.Contains(witnesses, candidate => Convert.ToHexStringLower(candidate.VKey.Span) == CliVerificationKey);
        Assert.Contains(witnesses, candidate => candidate.VKey.Span.SequenceEqual(extendedKey.GetPublicKey().Key));
        foreach (VKeyWitness witness in witnesses)
        {
            AssertVerifiesTheBody(witness);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(96)]
    [InlineData(128)]
    public void Constructor_RejectsOtherLengths(int length)
    {
        ArgumentException error = Assert.Throws<ArgumentException>("key", () => new PrivateKey(new byte[length], new byte[32]));
        Assert.StartsWith($"Must be 32 or 64 bytes, not {length}.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsNullKey() =>
        _ = Assert.Throws<ArgumentNullException>("key", () => new PrivateKey(null!, new byte[32]));

    [Theory]
    [InlineData(DerivationType.SOFT)]
    [InlineData(DerivationType.HARD)]
    public void Derive_FromSeedKey_ThrowsTheDocumentedError(DerivationType type)
    {
        PrivateKey key = new(Convert.FromHexString(CliSeed), new byte[32]);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => key.Derive(0, type));
        Assert.Contains("64-byte Cardano extended key", error.Message, StringComparison.Ordinal);
    }

    // A negative index aliased a hardened one: -1 SOFT was hardened 2^31 - 1, int.MinValue HARD was hardened 0.
    [Theory]
    [InlineData(-1, DerivationType.SOFT)]
    [InlineData(int.MinValue, DerivationType.SOFT)]
    [InlineData(-1, DerivationType.HARD)]
    [InlineData(int.MinValue, DerivationType.HARD)]
    public void Derive_NegativeIndex_Throws(int index, DerivationType type) =>
        _ = Assert.Throws<ArgumentOutOfRangeException>(nameof(index), () => CreateTestRootKey().Derive(index, type));

    [Fact]
    public void DeriveHelpers_RejectANegativeIndex()
    {
        PrivateKey root = CreateTestRootKey();
        _ = Assert.Throws<ArgumentOutOfRangeException>("account", () => root.DeriveCardanoAccountKey(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>("index", () => root.DerivePaymentKey(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>("index", () => root.DeriveStakeKey(-1));
    }

    [Fact]
    public void Derive_LargestSoftIndex_StaysSoft()
    {
        PrivateKey root = CreateTestRootKey();
        Assert.NotEqual(root.Derive(int.MaxValue, DerivationType.HARD).Key, root.Derive(int.MaxValue, DerivationType.SOFT).Key);
    }

    [Theory]
    [InlineData("m/1852'/1815'/0'/0/0", true)]
    [InlineData("m/1852'/1815'/-1'", false)]
    [InlineData("m/1852'/1815'/0'/0/-1", false)]
    [InlineData("", false)]
    [InlineData("m", false)]
    [InlineData("x/1", false)]
    [InlineData("M/0", false)]
    [InlineData("/0", false)]
    [InlineData("m/ 1", false)]
    [InlineData("m/1 ", false)]
    [InlineData("m/+1", false)]
    [InlineData("m/1852''", false)]
    [InlineData("m/'0", false)]
    [InlineData("m/1'2", false)]
    [InlineData("m/00", false)]
    [InlineData("m/01'", false)]
    [InlineData("m/1\0/2", false)]
    [InlineData("m/0", true)]
    [InlineData("m/0'", true)]
    [InlineData("m/2147483647'", true)]
    [InlineData("m/2147483647", true)]
    [InlineData("m/", false)]
    [InlineData("m/0/", false)]
    [InlineData("m//0", false)]
    [InlineData("m/'", false)]
    [InlineData("m/2147483648", false)]
    [InlineData("m/1852H/1815H/0H", false)]
    [InlineData("m/\u0661", false)]
    public void IsValidPath_ValidatesCanonicalBip32Paths(string path, bool valid) =>
        Assert.Equal(valid, Bip32Util.IsValidPath(path));

    [Fact]
    public void IsValidPath_Null_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>("path", () => Bip32Util.IsValidPath(null!));

    [Theory]
    [InlineData(0, PublicKeyTests.AccXPrv0)]
    [InlineData(1, PublicKeyTests.AccXPrv1)]
    public void DeriveCardanoAccountKey_MatchesCardanoAddresses410Accounts(int account, string expectedAccBech32)
    {
        byte[] rootRaw = PublicKeyTests.DecodeBech32Key(PublicKeyTests.RootXPrv);
        byte[] expectedRaw = PublicKeyTests.DecodeBech32Key(expectedAccBech32);
        PrivateKey root = new(rootRaw[..64], rootRaw[64..]);

        PrivateKey derived = root.DeriveCardanoAccountKey(account);

        Assert.Equal(expectedRaw[..64], derived.Key);
        Assert.Equal(expectedRaw[64..], derived.Chaincode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Derive_InvalidChaincode_ThrowsInvalidOperationException(int? chaincodeLength)
    {
        byte[] extendedKey = CreateTestRootKey().Key;
        byte[] chaincode = chaincodeLength is int length ? new byte[length] : null!;
        PrivateKey key = new(extendedKey, chaincode);

        _ = Assert.Throws<InvalidOperationException>(() => key.Derive(0, DerivationType.HARD));
        _ = Assert.Throws<InvalidOperationException>(() => key.Derive(0, DerivationType.SOFT));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void PointOfTrunc28Mul8_WrongLength_ThrowsArgumentException(int length) =>
        _ = Assert.Throws<ArgumentException>("sk", () => Bip32Util.PointOfTrunc28Mul8(new byte[length]));

    [Fact]
    public void PointOfTrunc28Mul8_IsThePublicKeyOfItsScalar()
    {
        byte[] z = Ed25519TestVectors.TestCases[0].Seed;
        byte[] scalar = Bip32Util.Add28Mul8(new byte[32], z);
        byte[] extendedKey = [.. scalar, .. z];
        byte[] message = [1, 2, 3];

        byte[] point = Bip32Util.PointOfTrunc28Mul8(z);

        Assert.Equal(Ed25519.GetPublicKey(extendedKey), point);
        Assert.True(Ed25519.Verify(Ed25519.SignCrypto(message, extendedKey), message, point));
    }

    private static ITransaction CliTransaction() =>
        ITransaction.Read(Convert.FromHexString("84" + CliBody + "a0f5f6"), Era.Conway);

    private static VKeyWitness[] Witnesses(ITransaction signed) =>
        [.. ((PostMaryTransaction)signed).Witnesses.VKeyWitnessSet() ?? []];

    private static void AssertVerifiesTheBody(VKeyWitness witness) =>
        Assert.True(Ed25519.Verify(witness.Signature.ToArray(), HashUtil.Blake2b256(Convert.FromHexString(CliBody)), witness.VKey.ToArray()));
}
