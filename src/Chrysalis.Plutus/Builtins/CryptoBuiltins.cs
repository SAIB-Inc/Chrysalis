using System.Collections.Immutable;
using Chrysalis.Plutus.Cek;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class CryptoBuiltins
{
    // --- Hash builtins ---

    internal static CekValue Sha2_256(ImmutableArray<CekValue> args) =>
        HashWith(new Sha256Digest(), UnwrapByteString(args[0]).Span);

    internal static CekValue Sha3_256(ImmutableArray<CekValue> args) =>
        HashWith(new Sha3Digest(256), UnwrapByteString(args[0]).Span);

    internal static CekValue Blake2b_256(ImmutableArray<CekValue> args) =>
        HashWith(new Blake2bDigest(256), UnwrapByteString(args[0]).Span);

    internal static CekValue Blake2b_224(ImmutableArray<CekValue> args) =>
        HashWith(new Blake2bDigest(224), UnwrapByteString(args[0]).Span);

    internal static CekValue Keccak_256(ImmutableArray<CekValue> args) =>
        HashWith(new KeccakDigest(256), UnwrapByteString(args[0]).Span);

    internal static CekValue Ripemd_160(ImmutableArray<CekValue> args) =>
        HashWith(new RipeMD160Digest(), UnwrapByteString(args[0]).Span);

    // --- Signature verification builtins ---

    internal static CekValue VerifyEd25519Signature(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> pk = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> msg = UnwrapByteString(args[1]);
        ReadOnlyMemory<byte> sig = UnwrapByteString(args[2]);

        if (pk.Length != Ed25519.PublicKeySize)
        {
            throw new EvaluationException(
                "verifyEd25519Signature: public key must be 32 bytes");
        }

        if (sig.Length != Ed25519.SignatureSize)
        {
            throw new EvaluationException(
                "verifyEd25519Signature: signature must be 64 bytes");
        }

        try
        {
            Ed25519PublicKeyParameters pubKey = new(pk.Span);
            Ed25519Signer signer = new();
            signer.Init(false, pubKey);
            signer.BlockUpdate(msg.Span);
            return BoolResult(signer.VerifySignature(sig.Span.ToArray()));
        }
        catch
        {
            return BoolResult(false);
        }
    }

    internal static CekValue VerifyEcdsaSecp256k1Signature(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> pk = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> msg = UnwrapByteString(args[1]);
        ReadOnlyMemory<byte> sig = UnwrapByteString(args[2]);

        if (pk.Length != 33)
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: public key must be 33 bytes");
        }

        byte prefix = pk.Span[0];
        if (prefix != 0x02 && prefix != 0x03)
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: public key must be compressed (prefix 0x02 or 0x03)");
        }

        if (msg.Length != 32)
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: message must be 32 bytes");
        }

        if (sig.Length != 64)
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: signature must be 64 bytes");
        }

        Org.BouncyCastle.Asn1.X9.X9ECParameters curve =
            Org.BouncyCastle.Crypto.EC.CustomNamedCurves.GetByName("secp256k1");

        Org.BouncyCastle.Math.EC.ECPoint point;
        try
        {
            point = curve.Curve.DecodePoint(pk.Span.ToArray());
        }
        catch
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: public key is not on the curve");
        }

        if (!point.IsValid())
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: public key is not on the curve");
        }

        ECPublicKeyParameters pubKey = new(point,
            new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H));

        // Extract r, s from raw 64-byte signature
        Org.BouncyCastle.Math.BigInteger r = new(1, sig[..32].Span.ToArray());
        Org.BouncyCastle.Math.BigInteger s = new(1, sig[32..].Span.ToArray());

        // Validate r, s in [1, n-1]
        if (r.SignValue <= 0 || r.CompareTo(curve.N) >= 0 ||
            s.SignValue <= 0 || s.CompareTo(curve.N) >= 0)
        {
            throw new EvaluationException(
                "verifyEcdsaSecp256k1Signature: r or s out of range");
        }

        // BIP-146 low-s check
        Org.BouncyCastle.Math.BigInteger halfOrder = curve.N.ShiftRight(1);
        if (s.CompareTo(halfOrder) > 0)
        {
            return BoolResult(false);
        }

        try
        {
            ECDsaSigner signer = new();
            signer.Init(false, pubKey);
            return BoolResult(signer.VerifySignature(msg.Span.ToArray(), r, s));
        }
        catch
        {
            return BoolResult(false);
        }
    }

    internal static CekValue VerifySchnorrSecp256k1Signature(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> pk = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> msg = UnwrapByteString(args[1]);
        ReadOnlyMemory<byte> sig = UnwrapByteString(args[2]);

        if (pk.Length != 32)
        {
            throw new EvaluationException(
                "verifySchnorrSecp256k1Signature: public key must be 32 bytes");
        }

        if (sig.Length != 64)
        {
            throw new EvaluationException(
                "verifySchnorrSecp256k1Signature: signature must be 64 bytes");
        }

        try
        {
            return BoolResult(Bip340Schnorr.Verify(
                pk.Span, msg.Span, sig.Span));
        }
        catch (EvaluationException)
        {
            throw;
        }
        catch
        {
            return BoolResult(false);
        }
    }

    // --- Private helpers ---

    private static CekValue HashWith(
        Org.BouncyCastle.Crypto.IDigest digest,
        ReadOnlySpan<byte> input)
    {
        byte[] inputArray = input.ToArray();
        digest.BlockUpdate(inputArray, 0, inputArray.Length);
        byte[] output = new byte[digest.GetDigestSize()];
        _ = digest.DoFinal(output, 0);
        return ByteStringResult(output);
    }
}
