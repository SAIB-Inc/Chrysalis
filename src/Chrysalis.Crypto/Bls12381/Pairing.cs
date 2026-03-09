using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// BLS12-381 pairing implementation: miller loop + final exponentiation.
/// Ported from noble-curves bls.ts createBlsPairing (MIT License, Paul Miller).
/// </summary>
internal static class Pairing
{
    // BLS_X seed and NAF decomposition
    private static readonly BigInteger BLS_X = BigInteger.Parse(
        "0d201000000010000",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    private const bool X_NEGATIVE = true; // BLS12-381 x is negative

    // Fp2(1/2)
    private static readonly Fp2Element Fp2div2 = Fp2.Inv(Fp2.MulScalar(Fp2.ONE, 2));

    // NAF decomposition of BLS_X
    private static readonly int[] ATE_NAF = ComputeNAF(BLS_X);

    /// <summary>
    /// Compute line function coefficients from point doubling on G2.
    /// Returns (c0, c1, c2) and updated R coordinates.
    /// </summary>
    private static (Fp2Element c0, Fp2Element c1, Fp2Element c2, Fp2Element Rx, Fp2Element Ry, Fp2Element Rz)
        LineDouble(Fp2Element Rx, Fp2Element Ry, Fp2Element Rz)
    {
        Fp2Element t0 = Fp2.Sqr(Ry);
        Fp2Element t1 = Fp2.Sqr(Rz);
        Fp2Element t2 = Fp2.MulByB(Fp2.MulScalar(t1, 3)); // 3 * T1 * B
        Fp2Element t3 = Fp2.MulScalar(t2, 3);              // 3 * T2
        Fp2Element t4 = Fp2.Sub(Fp2.Sub(Fp2.Sqr(Fp2.Add(Ry, Rz)), t1), t0);

        Fp2Element c0 = Fp2.Sub(t2, t0);
        Fp2Element c1 = Fp2.MulScalar(Fp2.Sqr(Rx), 3);
        Fp2Element c2 = Fp2.Neg(t4);

        // Update R
        Fp2Element newRx = Fp2.Mul(Fp2.Mul(Fp2.Mul(Fp2.Sub(t0, t3), Rx), Ry), Fp2div2);
        Fp2Element newRy = Fp2.Sub(
            Fp2.Sqr(Fp2.Mul(Fp2.Add(t0, t3), Fp2div2)),
            Fp2.MulScalar(Fp2.Sqr(t2), 3));
        Fp2Element newRz = Fp2.Mul(t0, t4);

        return (c0, c1, c2, newRx, newRy, newRz);
    }

    /// <summary>
    /// Compute line function coefficients from point addition on G2.
    /// </summary>
    private static (Fp2Element c0, Fp2Element c1, Fp2Element c2, Fp2Element Rx, Fp2Element Ry, Fp2Element Rz)
        LineAdd(Fp2Element Rx, Fp2Element Ry, Fp2Element Rz, Fp2Element Qx, Fp2Element Qy)
    {
        Fp2Element t0 = Fp2.Sub(Ry, Fp2.Mul(Qy, Rz));
        Fp2Element t1 = Fp2.Sub(Rx, Fp2.Mul(Qx, Rz));

        Fp2Element c0 = Fp2.Sub(Fp2.Mul(t0, Qx), Fp2.Mul(t1, Qy));
        Fp2Element c1 = Fp2.Neg(t0);
        Fp2Element c2 = t1;

        // Update R
        Fp2Element t2 = Fp2.Sqr(t1);
        Fp2Element t3 = Fp2.Mul(t2, t1);
        Fp2Element t4 = Fp2.Mul(t2, Rx);
        Fp2Element t5 = Fp2.Add(
            Fp2.Sub(t3, Fp2.MulScalar(t4, 2)),
            Fp2.Mul(Fp2.Sqr(t0), Rz));

        Fp2Element newRx = Fp2.Mul(t1, t5);
        Fp2Element newRy = Fp2.Sub(Fp2.Mul(Fp2.Sub(t4, t5), t0), Fp2.Mul(t3, Ry));
        Fp2Element newRz = Fp2.Mul(Rz, t3);

        return (c0, c1, c2, newRx, newRy, newRz);
    }

    /// <summary>
    /// Precompute line function coefficients for a G2 point.
    /// Returns array of (array of (c0, c1, c2)) for each step of the ate loop.
    /// </summary>
    internal static (Fp2Element c0, Fp2Element c1, Fp2Element c2)[][] CalcPairingPrecomputes(PointG2 point)
    {
        AffineG2 aff = point.ToAffine();
        Fp2Element Qx = aff.X, Qy = aff.Y, negQy = Fp2.Neg(aff.Y);
        Fp2Element Rx = Qx, Ry = Qy, Rz = Fp2.ONE;

        List<(Fp2Element, Fp2Element, Fp2Element)[]> ell = [];

        foreach (int bit in ATE_NAF)
        {
            List<(Fp2Element, Fp2Element, Fp2Element)> cur = [];

            // Double
            (Fp2Element c0, Fp2Element c1, Fp2Element c2, Fp2Element Rx, Fp2Element Ry, Fp2Element Rz) dbl = LineDouble(Rx, Ry, Rz);
            cur.Add((dbl.c0, dbl.c1, dbl.c2));
            Rx = dbl.Rx; Ry = dbl.Ry; Rz = dbl.Rz;

            // Add if bit is nonzero
            if (bit != 0)
            {
                Fp2Element addQy = bit == -1 ? negQy : Qy;
                (Fp2Element c0, Fp2Element c1, Fp2Element c2, Fp2Element Rx, Fp2Element Ry, Fp2Element Rz) add = LineAdd(Rx, Ry, Rz, Qx, addQy);
                cur.Add((add.c0, add.c1, add.c2));
                Rx = add.Rx; Ry = add.Ry; Rz = add.Rz;
            }

            ell.Add([.. cur]);
        }

        return [.. ell];
    }

    /// <summary>
    /// Apply sparse line function multiplication (multiplicative twist).
    /// f = f * (c0 + c1*Px + c2*Py)
    /// </summary>
    private static Fp12Element ApplyLine(
        Fp12Element f, Fp2Element c0, Fp2Element c1, Fp2Element c2,
        BigInteger Px, BigInteger Py) => Fp12.Mul014(f, c0, Fp2.MulScalar(c1, Px), Fp2.MulScalar(c2, Py));

    /// <summary>
    /// Compute miller loop for a batch of (G2 precomputes, G1 affine) pairs.
    /// </summary>
    internal static Fp12Element MillerLoopBatch(
        ((Fp2Element c0, Fp2Element c1, Fp2Element c2)[][] ell, BigInteger Px, BigInteger Py)[] pairs,
        bool withFinalExponent = false)
    {
        Fp12Element f12 = Fp12.ONE;

        if (pairs.Length > 0)
        {
            int ellLen = pairs[0].ell.Length;
            for (int i = 0; i < ellLen; i++)
            {
                f12 = Fp12.Sqr(f12);
                foreach (((Fp2Element c0, Fp2Element c1, Fp2Element c2)[][]? ell, BigInteger Px, BigInteger Py) in pairs)
                {
                    foreach ((Fp2Element c0, Fp2Element c1, Fp2Element c2) in ell[i])
                    {
                        f12 = ApplyLine(f12, c0, c1, c2, Px, Py);
                    }
                }
            }
        }

        if (X_NEGATIVE)
        {
            f12 = Fp12.Conjugate(f12);
        }

        return withFinalExponent ? Fp12.FinalExponentiate(f12) : f12;
    }

    /// <summary>
    /// Compute a single pairing e(g1, g2).
    /// </summary>
    internal static Fp12Element ComputePairing(PointG1 g1, PointG2 g2, bool withFinalExponent = true)
    {
        if (g1.Is0() || g2.Is0())
        {
            throw new ArgumentException("pairing is not available for ZERO point");
        }

        g1.AssertValidity();
        g2.AssertValidity();

        AffineG1 a1 = g1.ToAffine();
        (Fp2Element c0, Fp2Element c1, Fp2Element c2)[][] precomputes = CalcPairingPrecomputes(g2);

        return MillerLoopBatch(
            [(precomputes, a1.X, a1.Y)],
            withFinalExponent);
    }

    /// <summary>
    /// Compute product of pairings: ∏ e(g1_i, g2_i).
    /// </summary>
    internal static Fp12Element PairingBatch(
        (PointG1 g1, PointG2 g2)[] pairs,
        bool withFinalExponent = true)
    {
        ((Fp2Element, Fp2Element, Fp2Element)[][] ell, BigInteger Px, BigInteger Py)[] millerPairs = new ((Fp2Element, Fp2Element, Fp2Element)[][] ell, BigInteger Px, BigInteger Py)[pairs.Length];

        for (int i = 0; i < pairs.Length; i++)
        {
            (PointG1? g1, PointG2? g2) = pairs[i];
            if (g1.Is0() || g2.Is0())
            {
                throw new ArgumentException("pairing is not available for ZERO point");
            }

            g1.AssertValidity();
            g2.AssertValidity();

            AffineG1 a1 = g1.ToAffine();
            millerPairs[i] = (CalcPairingPrecomputes(g2), a1.X, a1.Y);
        }

        return MillerLoopBatch(millerPairs, withFinalExponent);
    }

    /// <summary>
    /// Verify pairing equality: e(P1, Q1) == e(P2, Q2)
    /// Equivalent to e(P1, Q1) * e(-P2, Q2) == 1
    /// </summary>
    internal static bool FinalVerify(Fp12Element lhs, Fp12Element rhs) =>
        Fp12.Eql(lhs, rhs);

    /// <summary>
    /// NAF (Non-Adjacent Form) decomposition.
    /// For BLS12-381, BLS_X has no sequential 1s so NAF is same as binary.
    /// </summary>
    private static int[] ComputeNAF(BigInteger a)
    {
        List<int> res = [];
        while (a > 1)
        {
            if ((a & 1) == 0)
            {
                res.Insert(0, 0);
            }
            else if ((a & 3) == 3)
            {
                res.Insert(0, -1);
                a += 1;
            }
            else
            {
                res.Insert(0, 1);
            }
            a >>= 1;
        }
        return [.. res];
    }
}
