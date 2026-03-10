using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Element of Fp12 = Fp6[w] / (w² - v), a quadratic extension of Fp6.
/// Represents c0 + c1*w.
/// </summary>
internal readonly record struct Fp12Element(Fp6Element C0, Fp6Element C1);

/// <summary>
/// Fp12 field arithmetic for BLS12-381.
/// Ported from noble-curves tower.ts _Field12 (MIT License, Paul Miller).
/// </summary>
internal static class Fp12
{
    internal static readonly Fp12Element ZERO = new(Fp6.ZERO, Fp6.ZERO);
    internal static readonly Fp12Element ONE = new(Fp6.ONE, Fp6.ZERO);

    // BLS12-381 seed parameter
    private static readonly BigInteger BLS_X = BigInteger.Parse(
        "0d201000000010000",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    private static readonly int BLS_X_LEN = FpUtils.BitLen(BLS_X);

    // Frobenius coefficients
    private static readonly Fp2Element[] FROBENIUS_COEFFICIENTS = ComputeFrobeniusCoeffs();

    internal static bool IsZero(Fp12Element a)
    {
        return Fp6.IsZero(a.C0) && Fp6.IsZero(a.C1);
    }

    internal static bool Eql(Fp12Element a, Fp12Element b)
    {
        return Fp6.Eql(a.C0, b.C0) && Fp6.Eql(a.C1, b.C1);
    }

    internal static Fp12Element Neg(Fp12Element a)
    {
        return new(Fp6.Neg(a.C0), Fp6.Neg(a.C1));
    }

    internal static Fp12Element Add(Fp12Element a, Fp12Element b)
    {
        return new(Fp6.Add(a.C0, b.C0), Fp6.Add(a.C1, b.C1));
    }

    internal static Fp12Element Sub(Fp12Element a, Fp12Element b)
    {
        return new(Fp6.Sub(a.C0, b.C0), Fp6.Sub(a.C1, b.C1));
    }

    internal static Fp12Element Mul(Fp12Element a, Fp12Element b)
    {
        Fp6Element t1 = Fp6.Mul(a.C0, b.C0);
        Fp6Element t2 = Fp6.Mul(a.C1, b.C1);
        return new(
            Fp6.Add(t1, Fp6.MulByNonresidue(t2)),
            Fp6.Sub(Fp6.Mul(Fp6.Add(a.C0, a.C1), Fp6.Add(b.C0, b.C1)), Fp6.Add(t1, t2))
        );
    }

    internal static Fp12Element MulScalar(Fp12Element a, BigInteger s)
    {
        return new(Fp6.MulScalar(a.C0, s), Fp6.MulScalar(a.C1, s));
    }

    internal static Fp12Element Sqr(Fp12Element a)
    {
        Fp6Element ab = Fp6.Mul(a.C0, a.C1);
        return new(
            Fp6.Sub(
                Fp6.Sub(
                    Fp6.Mul(Fp6.Add(Fp6.MulByNonresidue(a.C1), a.C0), Fp6.Add(a.C0, a.C1)),
                    ab),
                Fp6.MulByNonresidue(ab)),
            Fp6.Add(ab, ab)
        );
    }

    internal static Fp12Element Inv(Fp12Element a)
    {
        Fp6Element t = Fp6.Inv(Fp6.Sub(Fp6.Sqr(a.C0), Fp6.MulByNonresidue(Fp6.Sqr(a.C1))));
        return new(Fp6.Mul(a.C0, t), Fp6.Neg(Fp6.Mul(a.C1, t)));
    }

    internal static Fp12Element Div(Fp12Element a, Fp12Element b)
    {
        return Mul(a, Inv(b));
    }

    internal static Fp12Element Pow(Fp12Element num, BigInteger power)
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

        Fp12Element p = ONE;
        Fp12Element d = num;
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

    internal static Fp12Element Conjugate(Fp12Element a)
    {
        return new(a.C0, Fp6.Neg(a.C1));
    }

    /// <summary>
    /// Sparse multiplication for BLS12-381 (multiplicative twist).
    /// Multiplies by element with structure [o0, o1, 0, 0, o4, 0] in Fp12.
    /// </summary>
    internal static Fp12Element Mul014(Fp12Element a, Fp2Element o0, Fp2Element o1, Fp2Element o4)
    {
        Fp6Element t0 = Fp6.Mul01(a.C0, o0, o1);
        Fp6Element t1 = Fp6.Mul1(a.C1, o4);
        return new(
            Fp6.Add(Fp6.MulByNonresidue(t1), t0),
            Fp6.Sub(Fp6.Sub(Fp6.Mul01(Fp6.Add(a.C1, a.C0), o0, Fp2.Add(o1, o4)), t0), t1)
        );
    }

    internal static Fp12Element FrobeniusMap(Fp12Element a, int power)
    {
        Fp6Element frobC1 = Fp6.FrobeniusMap(a.C1, power);
        Fp2Element coeff = FROBENIUS_COEFFICIENTS[power % 12];
        return new(
            Fp6.FrobeniusMap(a.C0, power),
            new Fp6Element(
                Fp2.Mul(frobC1.C0, coeff),
                Fp2.Mul(frobC1.C1, coeff),
                Fp2.Mul(frobC1.C2, coeff))
        );
    }

    /// <summary>
    /// Cyclotomic squaring — optimized for elements in cyclotomic subgroup.
    /// </summary>
    internal static Fp12Element CyclotomicSquare(Fp12Element a)
    {
        Fp2Element c0c0 = a.C0.C0, c0c1 = a.C0.C1, c0c2 = a.C0.C2;
        Fp2Element c1c0 = a.C1.C0, c1c1 = a.C1.C1, c1c2 = a.C1.C2;

        (Fp2Element t3, Fp2Element t4) = Fp2.Fp4Square(c0c0, c1c1);
        (Fp2Element t5, Fp2Element t6) = Fp2.Fp4Square(c1c0, c0c2);
        (Fp2Element t7, Fp2Element t8) = Fp2.Fp4Square(c0c1, c1c2);

        Fp2Element t9 = Fp2.MulByNonresidue(t8);
        return new(
            new Fp6Element(
                Fp2.Add(Fp2.MulScalar(Fp2.Sub(t3, c0c0), 2), t3),
                Fp2.Add(Fp2.MulScalar(Fp2.Sub(t5, c0c1), 2), t5),
                Fp2.Add(Fp2.MulScalar(Fp2.Sub(t7, c0c2), 2), t7)),
            new Fp6Element(
                Fp2.Add(Fp2.MulScalar(Fp2.Add(t9, c1c0), 2), t9),
                Fp2.Add(Fp2.MulScalar(Fp2.Add(t4, c1c1), 2), t4),
                Fp2.Add(Fp2.MulScalar(Fp2.Add(t6, c1c2), 2), t6))
        );
    }

    /// <summary>
    /// Cyclotomic exponentiation using the BLS_X seed bits.
    /// </summary>
    internal static Fp12Element CyclotomicExp(Fp12Element num, BigInteger n)
    {
        Fp12Element z = ONE;
        for (int i = BLS_X_LEN - 1; i >= 0; i--)
        {
            z = CyclotomicSquare(z);
            if (FpUtils.BitGet(n, i))
            {
                z = Mul(z, num);
            }
        }
        return z;
    }

    /// <summary>
    /// BLS12-381 final exponentiation.
    /// Ported from noble-curves bls12-381.ts Fp12finalExponentiate.
    /// </summary>
    internal static Fp12Element FinalExponentiate(Fp12Element num)
    {
        BigInteger x = BLS_X;
        // this^(q⁶) / this
        Fp12Element t0 = Div(FrobeniusMap(num, 6), num);
        // t0^(q²) * t0
        Fp12Element t1 = Mul(FrobeniusMap(t0, 2), t0);
        Fp12Element t2 = Conjugate(CyclotomicExp(t1, x));
        Fp12Element t3 = Mul(Conjugate(CyclotomicSquare(t1)), t2);
        Fp12Element t4 = Conjugate(CyclotomicExp(t3, x));
        Fp12Element t5 = Conjugate(CyclotomicExp(t4, x));
        Fp12Element t6 = Mul(Conjugate(CyclotomicExp(t5, x)), CyclotomicSquare(t2));
        Fp12Element t7 = Conjugate(CyclotomicExp(t6, x));
        Fp12Element t2_t5_pow_q2 = FrobeniusMap(Mul(t2, t5), 2);
        Fp12Element t4_t1_pow_q3 = FrobeniusMap(Mul(t4, t1), 3);
        Fp12Element t6_t1c_pow_q1 = FrobeniusMap(Mul(t6, Conjugate(t1)), 1);
        Fp12Element t7_t3c_t1 = Mul(Mul(t7, Conjugate(t3)), t1);
        return Mul(Mul(Mul(t2_t5_pow_q2, t4_t1_pow_q3), t6_t1c_pow_q1), t7_t3c_t1);
    }

    internal static byte[] ToBytes(Fp12Element a)
    {
        return [.. Fp6.ToBytes(a.C0), .. Fp6.ToBytes(a.C1)];
    }

    internal static Fp12Element FromBytes(ReadOnlySpan<byte> bytes)
    {
        int f6 = Fp.BYTES * 6; // Fp6 byte size = 6 * 48 = 288
        return new(Fp6.FromBytes(bytes[..f6]), Fp6.FromBytes(bytes[f6..]));
    }

    private static Fp2Element[] ComputeFrobeniusCoeffs()
    {
        // calcFrobeniusCoefficients(Fp2, NONRESIDUE, p, 12, 1, 6)[0]
        BigInteger p = Fp.P;
        Fp2Element nr = Fp2.NONRESIDUE;
        BigInteger divisor = 6;
        BigInteger groupOrder = BigInteger.Pow(p, 2) - 1; // |Fp2*| = p² - 1
        Fp2Element[] coeffs = new Fp2Element[12];
        for (int j = 0; j < 12; j++)
        {
            BigInteger qPower = BigInteger.Pow(p, j);
            BigInteger power = FpUtils.PosMod((qPower - 1) / divisor, groupOrder);
            coeffs[j] = Fp2.Pow(nr, power);
        }
        return coeffs;
    }
}
