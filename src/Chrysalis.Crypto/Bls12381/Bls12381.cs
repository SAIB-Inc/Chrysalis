using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Top-level BLS12-381 API for Plutus VM builtins.
/// Provides compress/uncompress, group operations, pairing, hash-to-curve,
/// and Fp12 serialization.
/// </summary>
public static class Bls12381
{
    /// <summary>Compressed G1 point size in bytes.</summary>
    public const int G1CompressedSize = 48;

    /// <summary>Compressed G2 point size in bytes.</summary>
    public const int G2CompressedSize = 96;

    /// <summary>Fp12 element size in bytes.</summary>
    public const int Fp12Size = 576; // 12 * 48

    #region G1 Operations

    /// <summary>Compress a G1 point to 48 bytes.</summary>
    public static byte[] G1Compress(byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return UncompressG1(point).Compress();
    }

    /// <summary>Uncompress a G1 point from 48 bytes.</summary>
    public static byte[] G1Uncompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        PointG1 p = PointG1.Uncompress(compressed);
        AffineG1 a = p.ToAffine();
        byte[] xBytes = FpUtils.NumberToBytesBE(a.X, 48);
        byte[] yBytes = FpUtils.NumberToBytesBE(a.Y, 48);
        return [.. xBytes, .. yBytes];
    }

    /// <summary>Add two G1 points.</summary>
    public static byte[] G1Add(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        PointG1 pa = UncompressG1(a);
        PointG1 pb = UncompressG1(b);
        return pa.Add(pb).Compress();
    }

    /// <summary>Negate a G1 point.</summary>
    public static byte[] G1Neg(byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        PointG1 p = UncompressG1(point);
        return p.Negate().Compress();
    }

    /// <summary>Scalar multiplication on G1.</summary>
    public static byte[] G1ScalarMul(BigInteger scalar, byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        PointG1 p = UncompressG1(point);
        if (scalar == 0 || p.Is0())
        {
            return PointG1.ZERO.Compress();
        }

        BigInteger s = FpUtils.PosMod(scalar, PointG1.N);
        return s == 0 ? PointG1.ZERO.Compress() : p.Multiply(s).Compress();
    }

    /// <summary>Check equality of two G1 points.</summary>
    public static bool G1Equal(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        PointG1 pa = UncompressG1(a);
        PointG1 pb = UncompressG1(b);
        return pa.Equals(pb);
    }

    /// <summary>Hash to G1 group element.</summary>
    public static byte[] G1HashToGroup(byte[] msg, byte[] dst)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(dst);
        return HashToCurve.HashToG1(msg, dst).Compress();
    }

    private static PointG1 UncompressG1(byte[] bytes)
    {
        if (bytes.Length == G1CompressedSize)
        {
            return PointG1.Uncompress(bytes);
        }

        if (bytes.Length == 96)
        {
            BigInteger x = FpUtils.BytesToNumberBE(bytes.AsSpan(0, 48));
            BigInteger y = FpUtils.BytesToNumberBE(bytes.AsSpan(48, 48));
            return PointG1.FromAffine(Fp.Create(x), Fp.Create(y));
        }

        throw new ArgumentException($"Invalid G1 point size: {bytes.Length}");
    }

    #endregion

    #region G2 Operations

    /// <summary>Compress a G2 point to 96 bytes.</summary>
    public static byte[] G2Compress(byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return UncompressG2(point).Compress();
    }

    /// <summary>Uncompress a G2 point from 96 bytes.</summary>
    public static byte[] G2Uncompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        PointG2 p = PointG2.Uncompress(compressed);
        AffineG2 a = p.ToAffine();
        return [
            .. FpUtils.NumberToBytesBE(a.X.C1, 48),
            .. FpUtils.NumberToBytesBE(a.X.C0, 48),
            .. FpUtils.NumberToBytesBE(a.Y.C1, 48),
            .. FpUtils.NumberToBytesBE(a.Y.C0, 48)
        ];
    }

    /// <summary>Add two G2 points.</summary>
    public static byte[] G2Add(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        PointG2 pa = UncompressG2(a);
        PointG2 pb = UncompressG2(b);
        return pa.Add(pb).Compress();
    }

    /// <summary>Negate a G2 point.</summary>
    public static byte[] G2Neg(byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        PointG2 p = UncompressG2(point);
        return p.Negate().Compress();
    }

    /// <summary>Scalar multiplication on G2.</summary>
    public static byte[] G2ScalarMul(BigInteger scalar, byte[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        PointG2 p = UncompressG2(point);
        if (scalar == 0 || p.Is0())
        {
            return PointG2.ZERO.Compress();
        }

        BigInteger s = FpUtils.PosMod(scalar, PointG2.N);
        return s == 0 ? PointG2.ZERO.Compress() : p.Multiply(s).Compress();
    }

    /// <summary>Check equality of two G2 points.</summary>
    public static bool G2Equal(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        PointG2 pa = UncompressG2(a);
        PointG2 pb = UncompressG2(b);
        return pa.Equals(pb);
    }

    /// <summary>Hash to G2 group element.</summary>
    public static byte[] G2HashToGroup(byte[] msg, byte[] dst)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(dst);
        return HashToCurve.HashToG2(msg, dst).Compress();
    }

    private static PointG2 UncompressG2(byte[] bytes)
    {
        if (bytes.Length == G2CompressedSize)
        {
            return PointG2.Uncompress(bytes);
        }

        if (bytes.Length == 192)
        {
            BigInteger xc1 = FpUtils.BytesToNumberBE(bytes.AsSpan(0, 48));
            BigInteger xc0 = FpUtils.BytesToNumberBE(bytes.AsSpan(48, 48));
            BigInteger yc1 = FpUtils.BytesToNumberBE(bytes.AsSpan(96, 48));
            BigInteger yc0 = FpUtils.BytesToNumberBE(bytes.AsSpan(144, 48));
            Fp2Element x = Fp2.Create(Fp.Create(xc0), Fp.Create(xc1));
            Fp2Element y = Fp2.Create(Fp.Create(yc0), Fp.Create(yc1));
            return PointG2.FromAffine(x, y);
        }

        throw new ArgumentException($"Invalid G2 point size: {bytes.Length}");
    }

    #endregion

    #region Miller Loop / Pairing

    /// <summary>Compute BLS12-381 miller loop.</summary>
    public static byte[] MillerLoop(byte[] g1, byte[] g2)
    {
        ArgumentNullException.ThrowIfNull(g1);
        ArgumentNullException.ThrowIfNull(g2);
        PointG1 p1 = UncompressG1(g1);
        PointG2 p2 = UncompressG2(g2);

        if (p1.Is0() || p2.Is0())
        {
            throw new ArgumentException("pairing is not available for ZERO point");
        }

        p1.AssertValidity();
        p2.AssertValidity();

        AffineG1 a1 = p1.ToAffine();
        (Fp2Element c0, Fp2Element c1, Fp2Element c2)[][] precomputes = Pairing.CalcPairingPrecomputes(p2);
        Fp12Element result = Pairing.MillerLoopBatch(
            [(precomputes, a1.X, a1.Y)],
            withFinalExponent: false);

        return Fp12.ToBytes(result);
    }

    /// <summary>Multiply two miller loop results in Fp12.</summary>
    public static byte[] MulMlResult(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Fp12Element fa = Fp12.FromBytes(a);
        Fp12Element fb = Fp12.FromBytes(b);
        return Fp12.ToBytes(Fp12.Mul(fa, fb));
    }

    /// <summary>Verify pairing equality after final exponentiation.</summary>
    public static bool FinalVerify(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Fp12Element fa = Fp12.FinalExponentiate(Fp12.FromBytes(a));
        Fp12Element fb = Fp12.FinalExponentiate(Fp12.FromBytes(b));
        return Fp12.Eql(fa, fb);
    }

    #endregion
}
