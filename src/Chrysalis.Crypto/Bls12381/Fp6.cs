using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Element of Fp6 = Fp2[v] / (v³ - (u+1)), a cubic extension of Fp2.
/// Represents c0 + c1*v + c2*v².
/// </summary>
internal readonly record struct Fp6Element(Fp2Element C0, Fp2Element C1, Fp2Element C2);

/// <summary>
/// Fp6 field arithmetic for BLS12-381.
/// Ported from noble-curves tower.ts _Field6 (MIT License, Paul Miller).
/// </summary>
internal static class Fp6
{
    internal static readonly Fp6Element ZERO = new(Fp2.ZERO, Fp2.ZERO, Fp2.ZERO);
    internal static readonly Fp6Element ONE = new(Fp2.ONE, Fp2.ZERO, Fp2.ZERO);

    // Frobenius coefficients (precomputed from calcFrobeniusCoefficients)
    private static readonly Fp2Element[] FROBENIUS_COEFFICIENTS_1 = ComputeFrobeniusCoeffs1();
    private static readonly Fp2Element[] FROBENIUS_COEFFICIENTS_2 = ComputeFrobeniusCoeffs2();

    internal static bool IsZero(Fp6Element a) => Fp2.IsZero(a.C0) && Fp2.IsZero(a.C1) && Fp2.IsZero(a.C2);

    internal static bool Eql(Fp6Element a, Fp6Element b) => Fp2.Eql(a.C0, b.C0) && Fp2.Eql(a.C1, b.C1) && Fp2.Eql(a.C2, b.C2);

    internal static Fp6Element Neg(Fp6Element a) => new(Fp2.Neg(a.C0), Fp2.Neg(a.C1), Fp2.Neg(a.C2));

    internal static Fp6Element Add(Fp6Element a, Fp6Element b) => new(Fp2.Add(a.C0, b.C0), Fp2.Add(a.C1, b.C1), Fp2.Add(a.C2, b.C2));

    internal static Fp6Element Sub(Fp6Element a, Fp6Element b) => new(Fp2.Sub(a.C0, b.C0), Fp2.Sub(a.C1, b.C1), Fp2.Sub(a.C2, b.C2));

    internal static Fp6Element MulScalar(Fp6Element a, BigInteger s) => new(Fp2.MulScalar(a.C0, s), Fp2.MulScalar(a.C1, s), Fp2.MulScalar(a.C2, s));

    internal static Fp6Element Mul(Fp6Element a, Fp6Element b)
    {
        Fp2Element t0 = Fp2.Mul(a.C0, b.C0);
        Fp2Element t1 = Fp2.Mul(a.C1, b.C1);
        Fp2Element t2 = Fp2.Mul(a.C2, b.C2);
        return new(
            // t0 + ((c1+c2)*(r1+r2) - t1 - t2) * NonResidue
            Fp2.Add(t0, Fp2.MulByNonresidue(
                Fp2.Sub(Fp2.Mul(Fp2.Add(a.C1, a.C2), Fp2.Add(b.C1, b.C2)),
                    Fp2.Add(t1, t2)))),
            // (c0+c1)*(r0+r1) - t0 - t1 + t2*NonResidue
            Fp2.Add(
                Fp2.Sub(Fp2.Mul(Fp2.Add(a.C0, a.C1), Fp2.Add(b.C0, b.C1)),
                    Fp2.Add(t0, t1)),
                Fp2.MulByNonresidue(t2)),
            // t1 + (c0+c2)*(r0+r2) - t0 + t2  (note: subtracts t2 in original but this is -t0+t2 net)
            Fp2.Sub(Fp2.Add(t1, Fp2.Mul(Fp2.Add(a.C0, a.C2), Fp2.Add(b.C0, b.C2))),
                Fp2.Add(t0, t2))
        );
    }

    internal static Fp6Element Sqr(Fp6Element a)
    {
        Fp2Element t0 = Fp2.Sqr(a.C0);
        Fp2Element t1 = Fp2.MulScalar(Fp2.Mul(a.C0, a.C1), 2);
        Fp2Element t3 = Fp2.MulScalar(Fp2.Mul(a.C1, a.C2), 2);
        Fp2Element t4 = Fp2.Sqr(a.C2);
        return new(
            Fp2.Add(Fp2.MulByNonresidue(t3), t0),
            Fp2.Add(Fp2.MulByNonresidue(t4), t1),
            Fp2.Sub(Fp2.Sub(
                Fp2.Add(Fp2.Add(t1, Fp2.Sqr(Fp2.Add(Fp2.Sub(a.C0, a.C1), a.C2))), t3),
                t0), t4)
        );
    }

    internal static Fp6Element Inv(Fp6Element a)
    {
        Fp2Element t0 = Fp2.Sub(Fp2.Sqr(a.C0), Fp2.MulByNonresidue(Fp2.Mul(a.C2, a.C1)));
        Fp2Element t1 = Fp2.Sub(Fp2.MulByNonresidue(Fp2.Sqr(a.C2)), Fp2.Mul(a.C0, a.C1));
        Fp2Element t2 = Fp2.Sub(Fp2.Sqr(a.C1), Fp2.Mul(a.C0, a.C2));
        Fp2Element t4 = Fp2.Inv(Fp2.Add(
            Fp2.MulByNonresidue(Fp2.Add(Fp2.Mul(a.C2, t1), Fp2.Mul(a.C1, t2))),
            Fp2.Mul(a.C0, t0)));
        return new(Fp2.Mul(t4, t0), Fp2.Mul(t4, t1), Fp2.Mul(t4, t2));
    }

    internal static Fp6Element Div(Fp6Element a, Fp6Element b) => Mul(a, Inv(b));

    internal static Fp6Element Pow(Fp6Element num, BigInteger power)
    {
        if (power < 0)
        {
            throw new ArgumentException("negative exponent");
        }

        if (power == 0)
        {
            return ONE;
        }

        if (power == 1)
        {
            return num;
        }

        Fp6Element p = ONE;
        Fp6Element d = num;
        while (power > 0)
        {
            if ((power & 1) == 1)
            {
                p = Mul(p, d);
            }

            d = Sqr(d);
            power >>= 1;
        }
        return p;
    }

    /// <summary>
    /// Cyclic shift: mulByNonresidue({c0,c1,c2}) = {nonresidue*c2, c0, c1}
    /// </summary>
    internal static Fp6Element MulByNonresidue(Fp6Element a) => new(Fp2.MulByNonresidue(a.C2), a.C0, a.C1);

    /// <summary>
    /// Sparse multiplication by (b1) in position 1: {0, b1, 0}
    /// </summary>
    internal static Fp6Element Mul1(Fp6Element a, Fp2Element b1) => new(
            Fp2.MulByNonresidue(Fp2.Mul(a.C2, b1)),
            Fp2.Mul(a.C0, b1),
            Fp2.Mul(a.C1, b1)
        );

    /// <summary>
    /// Sparse multiplication by {b0, b1, 0}
    /// </summary>
    internal static Fp6Element Mul01(Fp6Element a, Fp2Element b0, Fp2Element b1)
    {
        Fp2Element t0 = Fp2.Mul(a.C0, b0);
        Fp2Element t1 = Fp2.Mul(a.C1, b1);
        return new(
            Fp2.Add(Fp2.MulByNonresidue(Fp2.Sub(Fp2.Mul(Fp2.Add(a.C1, a.C2), b1), t1)), t0),
            Fp2.Sub(Fp2.Sub(Fp2.Mul(Fp2.Add(b0, b1), Fp2.Add(a.C0, a.C1)), t0), t1),
            Fp2.Add(Fp2.Sub(Fp2.Mul(Fp2.Add(a.C0, a.C2), b0), t0), t1)
        );
    }

    internal static Fp6Element MulByFp2(Fp6Element a, Fp2Element rhs) => new(Fp2.Mul(a.C0, rhs), Fp2.Mul(a.C1, rhs), Fp2.Mul(a.C2, rhs));

    internal static Fp6Element FrobeniusMap(Fp6Element a, int power) => new(
            Fp2.FrobeniusMap(a.C0, power),
            Fp2.Mul(Fp2.FrobeniusMap(a.C1, power), FROBENIUS_COEFFICIENTS_1[power % 6]),
            Fp2.Mul(Fp2.FrobeniusMap(a.C2, power), FROBENIUS_COEFFICIENTS_2[power % 6])
        );

    internal static byte[] ToBytes(Fp6Element a) => [.. Fp2.ToBytes(a.C0), .. Fp2.ToBytes(a.C1), .. Fp2.ToBytes(a.C2)];

    internal static Fp6Element FromBytes(ReadOnlySpan<byte> bytes)
    {
        int b2 = Fp.BYTES * 2; // Fp2 byte size
        return new(
            Fp2.FromBytes(bytes[..b2]),
            Fp2.FromBytes(bytes[b2..(2 * b2)]),
            Fp2.FromBytes(bytes[(2 * b2)..])
        );
    }

    private static Fp2Element[] ComputeFrobeniusCoeffs1()
    {
        // calcFrobeniusCoefficients(Fp2, NONRESIDUE, p, 6, 2, 3)[0]
        BigInteger p = Fp.P;
        Fp2Element nr = Fp2.NONRESIDUE;
        BigInteger divisor = 3;
        BigInteger groupOrder = BigInteger.Pow(p, 2) - 1; // |Fp2*| = p² - 1
        Fp2Element[] coeffs = new Fp2Element[6];
        for (int j = 0; j < 6; j++)
        {
            BigInteger qPower = BigInteger.Pow(p, j);
            BigInteger power = FpUtils.PosMod((qPower - 1) / divisor, groupOrder);
            coeffs[j] = Fp2.Pow(nr, power);
        }
        return coeffs;
    }

    private static Fp2Element[] ComputeFrobeniusCoeffs2()
    {
        BigInteger p = Fp.P;
        Fp2Element nr = Fp2.NONRESIDUE;
        BigInteger divisor = 3;
        BigInteger groupOrder = BigInteger.Pow(p, 2) - 1;
        Fp2Element[] coeffs = new Fp2Element[6];
        for (int j = 0; j < 6; j++)
        {
            BigInteger qPower = BigInteger.Pow(p, j);
            BigInteger power = FpUtils.PosMod(2 * (qPower - 1) / divisor, groupOrder);
            coeffs[j] = Fp2.Pow(nr, power);
        }
        return coeffs;
    }
}
