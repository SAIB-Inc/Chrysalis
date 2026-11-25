using System.Globalization;
using System.Numerics;

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
    /// Ed25519 is malleable in the S part of the signature.
    /// Adding L (the order of the subgroup) once keeps S below 2^253 and the signature valid.
    /// Adding L twice exceeds 2^253 causing rejection.
    /// This documents *is* behavior, not necessarily *should* behavior.
    /// </summary>
    [Fact]
    public void MalleabilityAddL_OnceValid_TwiceInvalid()
    {
        Ed25519TestVector testCase = Ed25519TestVectors.TestCases[0];
        byte[] signature = Ed25519.Sign(testCase.Message, testCase.PrivateKey);
        Assert.True(Ed25519.Verify(signature, testCase.Message, testCase.PublicKey));

        byte[] modifiedSignature = AddLToSignature(signature);
        Assert.True(Ed25519.Verify(modifiedSignature, testCase.Message, testCase.PublicKey));

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
        byte[] hash = System.Security.Cryptography.SHA512.HashData(seed);
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
        BigInteger l = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493", CultureInfo.InvariantCulture);
        i += l;
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
