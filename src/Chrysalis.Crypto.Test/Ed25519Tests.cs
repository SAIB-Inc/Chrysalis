using System.Globalization;
using System.Numerics;

namespace Chrysalis.Crypto.Test;

public sealed class Ed25519Tests
{
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
        }
    }

    /// <summary>
    /// GetPublicKey is Cardano-specific: it takes an expanded key whose first 32 bytes are
    /// an already-clamped scalar (from BIP32 derivation). For standard Ed25519 seeds, we must
    /// first expand via ExpandedPrivateKeyFromSeed, then extract the public key from bytes 32-63.
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

    private static byte[] AddLToSignature(byte[] signature)
    {
        return [.. signature[..32], .. AddL(signature[32..])];
    }

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
