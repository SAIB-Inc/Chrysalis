using Org.BouncyCastle.Crypto.Digests;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// BIP-340 Schnorr signature verification for secp256k1.
/// Pure managed implementation using BouncyCastle EC primitives.
/// </summary>
internal static class Bip340Schnorr
{
    private static readonly Org.BouncyCastle.Asn1.X9.X9ECParameters Curve =
        Org.BouncyCastle.Crypto.EC.CustomNamedCurves.GetByName("secp256k1");

    private static readonly Org.BouncyCastle.Math.BigInteger P =
        Curve.Curve.Field.Characteristic;

    private static readonly Org.BouncyCastle.Math.BigInteger N = Curve.N;

    internal static bool Verify(
        ReadOnlySpan<byte> pubKeyX,
        ReadOnlySpan<byte> message,
        ReadOnlySpan<byte> signature)
    {
        // Step 1: Parse public key x-coordinate and lift to point
        Org.BouncyCastle.Math.BigInteger x = new(1, pubKeyX.ToArray());
        if (x.CompareTo(P) >= 0)
        {
            throw new Cek.EvaluationException(
                "verifySchnorrSecp256k1Signature: invalid public key");
        }

        Org.BouncyCastle.Math.EC.ECPoint point;
        try
        {
            point = LiftX(x);
        }
        catch
        {
            throw new Cek.EvaluationException(
                "verifySchnorrSecp256k1Signature: invalid public key");
        }

        // Step 2: Parse signature (r, s)
        Org.BouncyCastle.Math.BigInteger r = new(1, signature[..32].ToArray());
        Org.BouncyCastle.Math.BigInteger s = new(1, signature[32..].ToArray());

        if (r.CompareTo(P) >= 0 || s.CompareTo(N) >= 0)
        {
            return false;
        }

        // Step 3: Compute e = tagged_hash("BIP0340/challenge", r || pk || msg) mod n
        byte[] eBytes = TaggedHash("BIP0340/challenge",
            PadTo32(r), pubKeyX.ToArray(), message.ToArray());
        Org.BouncyCastle.Math.BigInteger e = new Org.BouncyCastle.Math.BigInteger(1, eBytes).Mod(N);

        // Step 4: Compute R = s*G - e*P
        Org.BouncyCastle.Math.EC.ECPoint sG = Curve.G.Multiply(s);
        Org.BouncyCastle.Math.EC.ECPoint eP = point.Multiply(e);
        Org.BouncyCastle.Math.EC.ECPoint rPoint = sG.Add(eP.Negate()).Normalize();

        if (rPoint.IsInfinity)
        {
            return false;
        }

        // Step 5: Verify R.y is even and R.x == r
        return !rPoint.AffineYCoord.ToBigInteger().TestBit(0)
            && rPoint.AffineXCoord.ToBigInteger().Equals(r);
    }

    private static Org.BouncyCastle.Math.EC.ECPoint LiftX(
        Org.BouncyCastle.Math.BigInteger x)
    {
        // y^2 = x^3 + 7 mod p
        Org.BouncyCastle.Math.BigInteger c = x.ModPow(
            Org.BouncyCastle.Math.BigInteger.Three, P)
            .Add(Org.BouncyCastle.Math.BigInteger.ValueOf(7)).Mod(P);

        // y = c^((p+1)/4) mod p (works because p ≡ 3 mod 4)
        Org.BouncyCastle.Math.BigInteger y = c.ModPow(
            P.Add(Org.BouncyCastle.Math.BigInteger.One).ShiftRight(2), P);

        if (!y.ModPow(Org.BouncyCastle.Math.BigInteger.Two, P).Equals(c))
        {
            throw new InvalidOperationException("No square root exists");
        }

        // Choose even y
        if (y.TestBit(0))
        {
            y = P.Subtract(y);
        }

        return Curve.Curve.CreatePoint(x, y);
    }

    private static byte[] TaggedHash(string tag, params byte[][] parts)
    {
        byte[] tagBytes = System.Text.Encoding.UTF8.GetBytes(tag);
        Sha256Digest sha = new();

        // tag_hash = SHA256(tag)
        byte[] tagHash = new byte[32];
        sha.BlockUpdate(tagBytes, 0, tagBytes.Length);
        _ = sha.DoFinal(tagHash, 0);

        // tagged_hash = SHA256(tag_hash || tag_hash || data...)
        sha.Reset();
        sha.BlockUpdate(tagHash, 0, 32);
        sha.BlockUpdate(tagHash, 0, 32);
        foreach (byte[] part in parts)
        {
            sha.BlockUpdate(part, 0, part.Length);
        }

        byte[] result = new byte[32];
        _ = sha.DoFinal(result, 0);
        return result;
    }

    private static byte[] PadTo32(Org.BouncyCastle.Math.BigInteger n)
    {
        byte[] bytes = n.ToByteArrayUnsigned();
        if (bytes.Length >= 32)
        {
            return bytes;
        }

        byte[] padded = new byte[32];
        bytes.CopyTo(padded, 32 - bytes.Length);
        return padded;
    }
}
