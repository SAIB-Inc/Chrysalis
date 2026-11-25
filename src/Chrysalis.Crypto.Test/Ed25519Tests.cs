using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Crypto.Internal.Ed25519Ref10;

namespace Chrysalis.Crypto.Test;

public sealed class Ed25519Tests
{
    private const string BasePointHex = "5866666666666666666666666666666666666666666666666666666666666666";
    private const string NegBasePointHex = "58666666666666666666666666666666666666666666666666666666666666e6";
    private const string TwoBasePointHex = "c9a3f86aae465f0e56513864510f3997561fa2c9e85ea21dc2292309f3cd6022";
    private const string IdentityHex = "0100000000000000000000000000000000000000000000000000000000000000";
    private const string Order2Hex = "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f";
    private const string Order4PosHex = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Order4NegHex = "0000000000000000000000000000000000000000000000000000000000000080";
    private const string OffCurveY2Hex = "0200000000000000000000000000000000000000000000000000000000000000";
    private const string UnreducedYPHex = "edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f";
    private const string UnreducedYPNegHex = "edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string UnreducedYP1Hex = "eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f";
    private const string UnreducedYP1NegHex = "eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string NonCanonicalX0Y1Hex = "0100000000000000000000000000000000000000000000000000000000000080";
    private const string NonCanonicalX0Ord2Hex = "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string HighYHex = "02ffffffffffffffffffffffffffff00ffffffffffffffffffffffffffffff7f";
    private const string HighYNegHex = "02ffffffffffffffffffffffffffff00ffffffffffffffffffffffffffffffff";
    private const string Order8AHex = "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a";
    private const string Order8BNegHex = "26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc05";
    private const string Order8BHex = "26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc85";
    private const string Order8ANegHex = "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa";
    private const string MixedOrder160Hex = "15deb30729eb3e465c88f11b57d0c8d36eb0f670c3b22d2f80ea6049eeb2c23b";

    // Test-only vector: RFC 8032 §7.1 TEST SHA(abc).
    private const string Rfc8032ShaAbcSeedHex = "833fe62409237b9d62ec77587520911e9a759cec1d19755b7da901b96dca3d42";
    private const string Rfc8032ShaAbcPublicKeyHex = "ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf";
    private const string Rfc8032ShaAbcMessageHex = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";
    private const string Rfc8032ShaAbcSignatureHex = "dc2a4459e7369633a52b1bf277839a00201009a3efbf3ecb69bea2186c26b58909351fc9ac90b3ecfdfbc7c66431e0303dca179c138ac17ad9bef1177331a704";

    private static readonly BigInteger SubgroupOrderL = (BigInteger.One << 252) +
        BigInteger.Parse("27742317777372353535851937790883648493", CultureInfo.InvariantCulture);
    private static readonly Lazy<(int DeclaredCount, WycheproofCase[] Cases)> Wycheproof = new(LoadWycheproof);

    private sealed record WycheproofCase(int Id, string Result, string[] Flags, byte[] Signature, byte[] Message, byte[] PublicKey)
    {
        public bool IsAcceptable(string flag) => Result == "acceptable" && Flags.Contains(flag);

        public bool Verify() => Ed25519.Verify(Signature, Message, PublicKey);
    }

    [Fact]
    public void TestVectors_Load_1024Cases()
    {
        Ed25519TestVectors.LoadTestCases();
        Assert.Equal(1024, Ed25519TestVectors.TestCases.Count);
    }

    [Fact]
    public void Sign_AllTestVectors_ProducesExpectedSignature()
    {
        foreach (Ed25519TestVector testCase in Ed25519TestVectors.TestCases)
        {
            byte[] sig = Ed25519.Sign(testCase.Message, testCase.PrivateKey);
            Assert.Equal(64, sig.Length);
            Assert.Equal(BitConverter.ToString(testCase.Signature), BitConverter.ToString(sig));
            Assert.Equal(BitConverter.ToString(testCase.Signature), BitConverter.ToString(Ed25519.SignWithSeedAndPublicKey(testCase.Message, testCase.PrivateKey)));
        }
    }

    [Fact]
    public void Verify_AllTestVectors_ReturnsTrue()
    {
        foreach (Ed25519TestVector testCase in Ed25519TestVectors.TestCases)
        {
            bool success = Ed25519.Verify(testCase.Signature, testCase.Message, testCase.PublicKey);
            Assert.True(success);
        }
    }

    [Fact]
    public void ExpandedPrivateKeyFromSeed_AllTestVectors_MatchesExpected()
    {
        foreach (Ed25519TestVector testCase in Ed25519TestVectors.TestCases)
        {
            byte[] expanded = Ed25519.ExpandedPrivateKeyFromSeed(testCase.Seed);
            Assert.Equal(64, expanded.Length);
            Assert.Equal(BitConverter.ToString(testCase.PrivateKey), BitConverter.ToString(expanded));
            Assert.Equal(BitConverter.ToString(testCase.PrivateKey), BitConverter.ToString(Ed25519.SeedAndPublicKeyFromSeed(testCase.Seed)));
            Assert.Equal(BitConverter.ToString(testCase.PublicKey), BitConverter.ToString(Ed25519.PublicKeyFromSeed(testCase.Seed)));
        }
    }

    /// <summary>
    /// GetPublicKey multiplies the base point by its first 32 bytes as they are: the scalar of a
    /// Cardano extended key, or the 32-byte scalar that Bip32Util.PointOfTrunc28Mul8 passes.
    /// A seed's public key comes from PublicKeyFromSeed instead.
    /// This test verifies GetPublicKey produces valid keys that can verify signatures.
    /// </summary>
    [Fact]
    public void GetPublicKey_FromClampedScalar_ProducesValidKey()
    {
        byte[] clampedScalar = CreateClampedScalar(new byte[32]);
        byte[] pk = Ed25519.GetPublicKey(clampedScalar);
        Assert.Equal(32, pk.Length);

        // Sign with the clamped key and verify with the derived public key
        byte[] message = [1, 2, 3, 4, 5];
        byte[] sig = Ed25519.SignCrypto(message, clampedScalar);
        Assert.True(Ed25519.Verify(sig, message, pk));
        Assert.Equal(pk, Ed25519.PublicKeyFromExtendedKey(clampedScalar));
        Assert.Equal(pk, Ed25519.GetPublicKey(clampedScalar[..32]));
    }

    [Fact]
    public void Verify_ModifiedMessage_ReturnsFalse()
    {
        Ed25519TestVector testCase = Ed25519TestVectors.TestCases[0];
        byte[] signature = Ed25519.Sign(testCase.Message, testCase.PrivateKey);

        // For empty messages, use a different test case with actual content
        if (testCase.Message.Length == 0)
        {
            testCase = Ed25519TestVectors.TestCases[1];
            signature = Ed25519.Sign(testCase.Message, testCase.PrivateKey);
        }

        Assert.True(Ed25519.Verify(signature, testCase.Message, testCase.PublicKey));

        foreach (byte[] modifiedMessage in WithChangedBit(testCase.Message))
        {
            Assert.False(Ed25519.Verify(signature, modifiedMessage, testCase.PublicKey));
        }
    }

    [Fact]
    public void Verify_ModifiedSignature_ReturnsFalse()
    {
        Ed25519TestVector testCase = Ed25519TestVectors.TestCases[1];
        byte[] signature = Ed25519.Sign(testCase.Message, testCase.PrivateKey);
        Assert.True(Ed25519.Verify(signature, testCase.Message, testCase.PublicKey));

        foreach (byte[] modifiedSignature in WithChangedBit(signature))
        {
            Assert.False(Ed25519.Verify(modifiedSignature, testCase.Message, testCase.PublicKey));
        }
    }

    /// <summary>
    /// Adding the subgroup order to S must invalidate an otherwise valid signature.
    /// </summary>
    [Fact]
    public void MalleabilityAddL_OnceAndTwiceInvalid()
    {
        Ed25519TestVector testCase = Ed25519TestVectors.TestCases[0];
        byte[] signature = Ed25519.Sign(testCase.Message, testCase.PrivateKey);
        Assert.True(Ed25519.Verify(signature, testCase.Message, testCase.PublicKey));

        byte[] modifiedSignature = AddLToSignature(signature);
        Assert.False(Ed25519.Verify(modifiedSignature, testCase.Message, testCase.PublicKey));

        byte[] modifiedSignature2 = AddLToSignature(modifiedSignature);
        Assert.False(Ed25519.Verify(modifiedSignature2, testCase.Message, testCase.PublicKey));
    }

    /// <summary>
    /// SignCrypto is Cardano-specific: it signs without re-hashing the nonce, for use with
    /// BIP32-Ed25519 expanded keys. For standard Ed25519 test vectors, SignCrypto produces
    /// different signatures than Sign because the nonce derivation differs.
    /// This test verifies SignCrypto + Verify round-trip works.
    /// </summary>
    [Fact]
    public void SignCrypto_RoundTrip_VerifiesSuccessfully()
    {
        // Use a clamped scalar for SignCrypto (the Cardano path)
        byte[] clampedScalar = CreateClampedScalar(42);
        byte[] pk = Ed25519.GetPublicKey(clampedScalar);
        byte[] message = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        byte[] sig = Ed25519.SignCrypto(message, clampedScalar);

        Assert.Equal(64, sig.Length);
        Assert.True(Ed25519.Verify(sig, message, pk));
        Assert.Equal(sig, Ed25519.SignWithExtendedKey(message, clampedScalar));
    }

    [Fact]
    public void SignCrypto_ModifiedMessage_VerifyFails()
    {
        byte[] clampedScalar = CreateClampedScalar(99);
        byte[] pk = Ed25519.GetPublicKey(clampedScalar);
        byte[] message = [1, 2, 3, 4, 5];
        byte[] sig = Ed25519.SignCrypto(message, clampedScalar);

        Assert.True(Ed25519.Verify(sig, message, pk));
        Assert.False(Ed25519.Verify(sig, [1, 2, 3, 4, 6], pk));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(96)]
    public void SixtyFourByteMembers_RejectOtherLengths(int length)
    {
        byte[] key = new byte[length];
        byte[] message = [1, 2, 3];
        _ = Assert.Throws<ArgumentException>("expandedPrivateKey", () => Ed25519.Sign(message, key));
        _ = Assert.Throws<ArgumentException>("expandedPrivateKey", () => Ed25519.SignCrypto(message, key));
        _ = Assert.Throws<ArgumentException>("seedAndPublicKey", () => Ed25519.SignWithSeedAndPublicKey(message, key));
        _ = Assert.Throws<ArgumentException>("extendedKey", () => Ed25519.SignWithExtendedKey(message, key));
        _ = Assert.Throws<ArgumentException>("extendedKey", () => Ed25519.PublicKeyFromExtendedKey(key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(65)]
    public void GetPublicKey_RejectsOtherLengths(int length) =>
        _ = Assert.Throws<ArgumentException>("expandedPrivateKey", () => Ed25519.GetPublicKey(new byte[length]));

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void SeedMembers_RejectOtherLengths(int length)
    {
        byte[] key = new byte[length];
        _ = Assert.Throws<ArgumentException>("privateKeySeed", () => Ed25519.ExpandedPrivateKeyFromSeed(key));
        _ = Assert.Throws<ArgumentException>("seed", () => Ed25519.SeedAndPublicKeyFromSeed(key));
        _ = Assert.Throws<ArgumentException>("seed", () => Ed25519.PublicKeyFromSeed(key));
    }

    [Fact]
    public void ExistingMembers_KeepTheirNullErrors()
    {
        byte[] message = [1, 2, 3];
        _ = Assert.Throws<ArgumentNullException>("expandedPrivateKey", () => Ed25519.Sign(message, null!));
        _ = Assert.Throws<ArgumentNullException>("expandedPrivateKey", () => Ed25519.SignCrypto(message, null!));
        _ = Assert.Throws<ArgumentNullException>("privateKeySeed", () => Ed25519.ExpandedPrivateKeyFromSeed(null!));
        _ = Assert.Throws<ArgumentNullException>("expandedPrivateKey", () => Ed25519.GetPublicKey(null!));

        byte[] signature = new byte[64];
        byte[] publicKey = new byte[32];
        _ = Assert.Throws<ArgumentNullException>("signature", () => Ed25519.Verify(null!, message, publicKey));
        _ = Assert.Throws<ArgumentNullException>("message", () => Ed25519.Verify(signature, null!, publicKey));
        _ = Assert.Throws<ArgumentNullException>("publicKey", () => Ed25519.Verify(signature, message, null!));
    }

    [Fact]
    public void PointAdd_ValidPoints_MatchesScalarAddition()
    {
        byte[] basePoint = Convert.FromHexString(BasePointHex);
        byte[] identity = Convert.FromHexString(IdentityHex);
        byte[] p5 = Ed25519.GetPublicKey(SmallScalar(5));
        byte[] p7 = Ed25519.GetPublicKey(SmallScalar(7));
        byte[] p12 = Ed25519.GetPublicKey(SmallScalar(12));
        byte[] p13 = Ed25519.GetPublicKey(SmallScalar(13));
        byte[] p25 = Ed25519.GetPublicKey(SmallScalar(25));

        Assert.Equal(basePoint, Ed25519.GetPublicKey(SmallScalar(1)));
        Assert.Equal(Convert.FromHexString(TwoBasePointHex), Ed25519.PointAdd(basePoint, basePoint));
        Assert.Equal(basePoint, Ed25519.PointAdd(identity, basePoint));
        Assert.Equal(basePoint, Ed25519.PointAdd(basePoint, identity));
        Assert.Equal(p12, Ed25519.PointAdd(p5, p7));
        Assert.Equal(p12, Ed25519.PointAdd(p7, p5));
        Assert.Equal(p25, Ed25519.PointAdd(Ed25519.PointAdd(p5, p7), p13));
        Assert.Equal(p25, Ed25519.PointAdd(p5, Ed25519.PointAdd(p7, p13)));
    }

    [Theory]
    [InlineData("B+(-B)=O", BasePointHex, NegBasePointHex, IdentityHex)]
    [InlineData("O+O=O", IdentityHex, IdentityHex, IdentityHex)]
    [InlineData("ord2+ord2=O", Order2Hex, Order2Hex, IdentityHex)]
    [InlineData("O+ord2=ord2", IdentityHex, Order2Hex, Order2Hex)]
    [InlineData("hiY+(-hiY)=O", HighYHex, HighYNegHex, IdentityHex)]
    public void PointAdd_ZeroXResults_AreCanonicallyEncoded(string caseName, string firstHex, string secondHex, string expectedHex)
    {
        _ = caseName;
        byte[] sum = Ed25519.PointAdd(Convert.FromHexString(firstHex), Convert.FromHexString(secondHex));

        Assert.Equal(Convert.FromHexString(expectedHex), sum);
    }

    [Fact]
    public void PointAdd_Order4Points_AreAcceptedAsCanonical()
    {
        byte[] q = Convert.FromHexString(Order4PosHex);
        byte[] negQ = Convert.FromHexString(Order4NegHex);
        byte[] order2 = Convert.FromHexString(Order2Hex);
        byte[] identity = Convert.FromHexString(IdentityHex);

        Assert.Equal(order2, Ed25519.PointAdd(q, q));
        Assert.Equal(order2, Ed25519.PointAdd(negQ, negQ));
        Assert.Equal(identity, Ed25519.PointAdd(q, negQ));
        Assert.Equal(identity, Ed25519.PointAdd(negQ, q));
    }

    [Theory]
    [InlineData("off-curve-y2-first", OffCurveY2Hex, true)]
    [InlineData("off-curve-y2-second", OffCurveY2Hex, false)]
    [InlineData("unreduced-p-pos-first", UnreducedYPHex, true)]
    [InlineData("unreduced-p-pos-second", UnreducedYPHex, false)]
    [InlineData("unreduced-p-neg-first", UnreducedYPNegHex, true)]
    [InlineData("unreduced-p-neg-second", UnreducedYPNegHex, false)]
    [InlineData("unreduced-p1-pos-first", UnreducedYP1Hex, true)]
    [InlineData("unreduced-p1-pos-second", UnreducedYP1Hex, false)]
    [InlineData("unreduced-p1-neg-first", UnreducedYP1NegHex, true)]
    [InlineData("unreduced-p1-neg-second", UnreducedYP1NegHex, false)]
    [InlineData("noncanonical-x0-y1-first", NonCanonicalX0Y1Hex, true)]
    [InlineData("noncanonical-x0-y1-second", NonCanonicalX0Y1Hex, false)]
    [InlineData("noncanonical-x0-ord2-first", NonCanonicalX0Ord2Hex, true)]
    [InlineData("noncanonical-x0-ord2-second", NonCanonicalX0Ord2Hex, false)]
    public void PointAdd_InvalidPoint_ThrowsArgumentException(string caseName, string invalidHex, bool invalidFirst)
    {
        _ = caseName;
        byte[] invalid = Convert.FromHexString(invalidHex);
        byte[] valid = Convert.FromHexString(BasePointHex);
        string expectedParam = invalidFirst ? "firstPoint" : "secondPoint";

        _ = Assert.Throws<ArgumentException>(expectedParam, () => invalidFirst ? Ed25519.PointAdd(invalid, valid) : Ed25519.PointAdd(valid, invalid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void PointAdd_WrongLength_ThrowsArgumentException(int length)
    {
        byte[] valid = Convert.FromHexString(BasePointHex);
        byte[] bad = new byte[length];

        _ = Assert.Throws<ArgumentException>("firstPoint", () => Ed25519.PointAdd(bad, valid));
        _ = Assert.Throws<ArgumentException>("secondPoint", () => Ed25519.PointAdd(valid, bad));
    }

    [Fact]
    public void PointAdd_NullInput_ThrowsArgumentNullException()
    {
        byte[] valid = Convert.FromHexString(BasePointHex);

        _ = Assert.Throws<ArgumentNullException>("firstPoint", () => Ed25519.PointAdd(null!, valid));
        _ = Assert.Throws<ArgumentNullException>("secondPoint", () => Ed25519.PointAdd(valid, null!));
    }

    [Fact]
    public void Rfc8032_Section71_TestShaAbc_SignAndVerify()
    {
        byte[] seed = Convert.FromHexString(Rfc8032ShaAbcSeedHex);
        byte[] publicKey = Convert.FromHexString(Rfc8032ShaAbcPublicKeyHex);
        byte[] message = Convert.FromHexString(Rfc8032ShaAbcMessageHex);
        byte[] signature = Convert.FromHexString(Rfc8032ShaAbcSignatureHex);

        Assert.Equal(publicKey, Ed25519.PublicKeyFromSeed(seed));
        Assert.Equal(signature, Ed25519.Sign(message, Ed25519.SeedAndPublicKeyFromSeed(seed)));
        Assert.True(Ed25519.Verify(signature, message, publicKey));
    }

    [Fact]
    public void Verify_Wycheproof_ValidVectors_ReturnTrue() =>
        AssertAllWycheproofReturn(c => c.Result == "valid", 117, true);

    [Fact]
    public void Verify_Wycheproof_WrongLengthSignaturesAndPublicKey_ThrowArgumentException()
    {
        WycheproofCase[] cases = [.. Wycheproof.Value.Cases.Where(c => c.Result == "invalid" && c.Signature.Length != 64)];
        Assert.Equal(12, cases.Length);
        foreach (WycheproofCase vector in cases)
        {
            _ = Assert.Throws<ArgumentException>("signature", () => vector.Verify());
        }

        Ed25519TestVector valid = Ed25519TestVectors.TestCases[0];
        foreach (int length in new[] { 31, 33 })
        {
            _ = Assert.Throws<ArgumentException>("publicKey", () => Ed25519.Verify(valid.Signature, valid.Message, new byte[length]));
        }
    }

    [Fact]
    public void Verify_Wycheproof_Invalid64ByteSignatures_ReturnFalse() =>
        AssertAllWycheproofReturn(c => c.Result == "invalid" && c.Signature.Length == 64, 52, false);

    [Fact]
    public void Verify_Wycheproof_SmallOrderPublicKeys_ReturnFalse() =>
        AssertAllWycheproofReturn(c => c.IsAcceptable("SmallOrderPublicKey"), 8, false);

    [Fact]
    public void Verify_Wycheproof_MixedOrderAndTotalAccounting_MatchesDocument()
    {
        (int declaredCount, WycheproofCase[] cases) = Wycheproof.Value;
        Assert.Equal(210, declaredCount);
        Assert.Equal(declaredCount, cases.Length);
        Assert.Equal(declaredCount, cases.Select(c => c.Id).Distinct().Count());
        Assert.All(cases, c => Assert.True(c.Result is "valid" or "invalid" ||
            c.IsAcceptable("SmallOrderPublicKey") ^ c.IsAcceptable("MixedOrderPublicKey"), $"Unclassified tcId={c.Id}"));

        WycheproofCase[] mixed = [.. cases.Where(c => c.IsAcceptable("MixedOrderPublicKey"))];
        Assert.Equal(Enumerable.Range(160, 21), mixed.Select(c => c.Id));
        // Accepted tcIds pinned from the red-first cofactorless verifier.
        int[] acceptedIds = [160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180];
        foreach (WycheproofCase vector in mixed)
        {
            Assert.True(vector.Verify() == acceptedIds.Contains(vector.Id), $"Unexpected verification result for tcId={vector.Id}");
        }
    }

    [Fact]
    public void Verify_SmallOrderR_ReturnsFalse()
    {
        // Construct a signature with R = O using the test-only RFC 8032 TEST 1 seed.
        Ed25519TestVector vector = Ed25519TestVectors.TestCases[0];
        byte[] r = Convert.FromHexString(IdentityHex);
        byte[] challenge = SHA512.HashData((byte[])[.. r, .. vector.PublicKey, .. vector.Message]);
        BigInteger k = new(challenge, isUnsigned: true);
        BigInteger a = new(CreateClampedScalar(vector.Seed).AsSpan(0, 32), isUnsigned: true);
        byte[] signature = new byte[64];
        r.CopyTo(signature, 0);
        Assert.True((k * a % SubgroupOrderL).TryWriteBytes(signature.AsSpan(32), out _, isUnsigned: true));

        Assert.False(Ed25519.Verify(signature, vector.Message, vector.PublicKey));
    }

    [Theory]
    [InlineData(UnreducedYP1Hex, BasePointHex)]
    [InlineData(NonCanonicalX0Y1Hex, BasePointHex)]
    [InlineData(UnreducedYPHex, IdentityHex)]
    [InlineData(NonCanonicalX0Ord2Hex, IdentityHex)]
    [InlineData(OffCurveY2Hex, IdentityHex)]
    public void Verify_NonCanonicalPublicKey_ReturnsFalse(string publicKeyHex, string rHex)
    {
        byte[] signature = [.. Convert.FromHexString(rHex), .. SmallScalar(1)];

        Assert.False(Ed25519.Verify(signature, [], Convert.FromHexString(publicKeyHex)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(959)]
    [InlineData(960)]
    [InlineData(961)]
    [InlineData(4096)]
    public void Verify_StackAndIncrementalHashBoundaryMessages_SignAndVerify(int length)
    {
        byte[] message = CreateBoundaryMessage(length);
        Ed25519TestVector vector = Ed25519TestVectors.TestCases[0];
        byte[] signature = Ed25519.Sign(message, vector.PrivateKey);
        byte[] extendedKey = CreateClampedScalar(vector.Seed);
        byte[] extendedPublicKey = Ed25519.GetPublicKey(extendedKey);
        byte[] extendedSignature = Ed25519.SignCrypto(message, extendedKey);

        Assert.True(Ed25519.Verify(signature, message, vector.PublicKey));
        Assert.True(Ed25519.Verify(extendedSignature, message, extendedPublicKey));
        if (length > 0)
        {
            message[^1] ^= 0x80;
            Assert.False(Ed25519.Verify(signature, message, vector.PublicKey));
            Assert.False(Ed25519.Verify(extendedSignature, message, extendedPublicKey));
        }
    }

    [Fact]
    public void Verify_InParallel_MatchesSequentialResultsWithNoSharedScratch()
    {
        Ed25519TestVector first = Ed25519TestVectors.TestCases[0];
        Ed25519TestVector longest = Ed25519TestVectors.TestCases[1023];
        byte[] stackMessage = CreateBoundaryMessage(960);
        byte[] streamMessage = CreateBoundaryMessage(961);
        byte[] stackSignature = Ed25519.Sign(stackMessage, first.PrivateKey);
        byte[] streamSignature = Ed25519.Sign(streamMessage, longest.PrivateKey);
        byte[] changedMessage = (byte[])streamMessage.Clone();
        changedMessage[^1] ^= 0x80;
        byte[] changedSignature = (byte[])stackSignature.Clone();
        changedSignature[0] ^= 0x80;
        (byte[] Signature, byte[] Message, byte[] PublicKey)[] cases =
        [
            (first.Signature, first.Message, first.PublicKey),
            (longest.Signature, longest.Message, longest.PublicKey),
            (Convert.FromHexString(Rfc8032ShaAbcSignatureHex), Convert.FromHexString(Rfc8032ShaAbcMessageHex), Convert.FromHexString(Rfc8032ShaAbcPublicKeyHex)),
            (stackSignature, stackMessage, first.PublicKey),
            (streamSignature, streamMessage, longest.PublicKey),
            (streamSignature, changedMessage, longest.PublicKey),
            (changedSignature, stackMessage, first.PublicKey),
        ];
        bool[] expected = [.. cases.Select(c => Ed25519.Verify(c.Signature, c.Message, c.PublicKey))];
        _ = Parallel.For(0, 64, i =>
        {
            int j = i % cases.Length;
            (byte[] signature, byte[] message, byte[] publicKey) = cases[j];
            Assert.Equal(expected[j], Ed25519.Verify(signature, message, publicKey));
        });
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("2^252-1", true)]
    [InlineData("L-1", true)]
    [InlineData("L-2^120", true)]
    [InlineData("L", false)]
    [InlineData("L+1", false)]
    [InlineData("L+2^120", false)]
    [InlineData("L+2^128", false)]
    [InlineData("0xff×32", false)]
    public void ScalarIsCanonical_BoundaryValues_MatchOrderL(string value, bool expected)
    {
        BigInteger scalar = value switch
        {
            "0" => BigInteger.Zero,
            "2^252-1" => (BigInteger.One << 252) - 1,
            "L-1" => SubgroupOrderL - 1,
            "L-2^120" => SubgroupOrderL - (BigInteger.One << 120),
            "L" => SubgroupOrderL,
            "L+1" => SubgroupOrderL + 1,
            "L+2^120" => SubgroupOrderL + (BigInteger.One << 120),
            "L+2^128" => SubgroupOrderL + (BigInteger.One << 128),
            "0xff×32" => (BigInteger.One << 256) - 1,
            _ => throw new ArgumentException("Unknown scalar boundary", nameof(value)),
        };
        byte[] bytes = new byte[32];
        Assert.True(scalar.TryWriteBytes(bytes, out _, isUnsigned: true));
        Assert.Equal(expected, ScalarOperations.sc_is_canonical(bytes));
    }

    [Theory]
    [InlineData("identity", IdentityHex, true)]
    [InlineData("order 2", Order2Hex, true)]
    [InlineData("order 4 positive", Order4PosHex, true)]
    [InlineData("order 4 negative", Order4NegHex, true)]
    [InlineData("order 8 A", Order8AHex, true)]
    [InlineData("order 8 -A", Order8ANegHex, true)]
    [InlineData("order 8 B", Order8BHex, true)]
    [InlineData("order 8 -B", Order8BNegHex, true)]
    [InlineData("base point", BasePointHex, false)]
    [InlineData("double base point", TwoBasePointHex, false)]
    [InlineData("high y", HighYHex, false)]
    [InlineData("mixed order tcId=160", MixedOrder160Hex, false)]
    public void HasSmallOrder_TorsionAndNonTorsionPoints_MatchExpected(string caseName, string pointHex, bool expected)
    {
        _ = caseName;
        Assert.Equal(0, GroupOperations.ge_frombytes_negate_vartime(out GroupElementP3 point, Convert.FromHexString(pointHex), 0));
        Assert.Equal(expected, Ed25519Operations.HasSmallOrder(ref point.X, ref point.Y));
    }

    private static byte[] CreateBoundaryMessage(int length)
    {
        byte[] message = new byte[length];
        for (int i = 0; i < message.Length; i++)
        {
            message[i] = (byte)((i * 31) | 1);
        }

        return message;
    }

    private static (int DeclaredCount, WycheproofCase[] Cases) LoadWycheproof()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Wycheproof", "ed25519_test.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        List<WycheproofCase> cases = [];
        foreach (JsonElement group in root.GetProperty("testGroups").EnumerateArray())
        {
            Assert.Equal("EddsaVerify", group.GetProperty("type").GetString());
            byte[] publicKey = Convert.FromHexString(RequiredString(group.GetProperty("publicKey"), "pk"));
            Assert.Equal(32, publicKey.Length);
            foreach (JsonElement entry in group.GetProperty("tests").EnumerateArray())
            {
                cases.Add(new WycheproofCase(entry.GetProperty("tcId").GetInt32(), RequiredString(entry, "result"),
                    [.. entry.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString() ?? throw new JsonException("Null flag"))],
                    Convert.FromHexString(RequiredString(entry, "sig")), Convert.FromHexString(RequiredString(entry, "msg")), publicKey));
            }
        }

        return (root.GetProperty("numberOfTests").GetInt32(), [.. cases]);
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"Null {property}");

    private static void AssertAllWycheproofReturn(Func<WycheproofCase, bool> select, int count, bool expected)
    {
        WycheproofCase[] cases = [.. Wycheproof.Value.Cases.Where(select)];
        Assert.Equal(count, cases.Length);
        int[] mismatches = [.. cases.Where(c => c.Verify() != expected).Select(c => c.Id)];
        Assert.True(mismatches.Length == 0, $"Expected {expected}; mismatched tcIds: {string.Join(", ", mismatches)}");
    }

    private static byte[] SmallScalar(byte value)
    {
        byte[] scalar = new byte[32];
        scalar[0] = value;
        return scalar;
    }

    private static byte[] CreateClampedScalar(byte seedByte)
    {
        byte[] seed = new byte[32];
        seed[0] = seedByte;
        return CreateClampedScalar(seed);
    }

    private static byte[] CreateClampedScalar(byte[] seed)
    {
        byte[] hash = SHA512.HashData(seed);
        byte[] clampedScalar = new byte[64];
        Array.Copy(hash, clampedScalar, 64);
        clampedScalar[0] &= 248;
        clampedScalar[31] &= 127;
        clampedScalar[31] |= 64;
        return clampedScalar;
    }

    private static byte[] AddL(IEnumerable<byte> input)
    {
        byte[] signedInput = [.. input, 0];
        BigInteger i = new(signedInput);
        i += SubgroupOrderL;
        return [.. i.ToByteArray().Concat(Enumerable.Repeat((byte)0, 32)).Take(32)];
    }

    private static byte[] AddLToSignature(byte[] signature) => [.. signature[..32], .. AddL(signature[32..])];

    private static IEnumerable<byte[]> WithChangedBit(byte[] array)
    {
        for (int i = 0; i < array.Length; i++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] result = (byte[])array.Clone();
                result[i] ^= (byte)(1 << bit);
                yield return result;
            }
        }
    }
}
