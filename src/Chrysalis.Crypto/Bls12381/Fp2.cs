using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Element of the quadratic extension field Fp2 = Fp[u] / (u² + 1).
/// Represents c0 + c1*u where u² = -1.
/// </summary>
internal readonly record struct Fp2Element(BigInteger C0, BigInteger C1);

/// <summary>
/// Fp2 field arithmetic for BLS12-381.
/// Ported from noble-curves tower.ts _Field2 (MIT License, Paul Miller).
/// </summary>
internal static class Fp2
{
    internal static readonly Fp2Element ZERO = new(BigInteger.Zero, BigInteger.Zero);
    internal static readonly Fp2Element ONE = new(BigInteger.One, BigInteger.Zero);

    // Non-residue for tower construction: (1 + u)
    internal static readonly Fp2Element NONRESIDUE = new(BigInteger.One, BigInteger.One);

    // Frobenius coefficients for Fp2: [1, p-1 mod p] = [1, -1 mod p]
    private static readonly BigInteger FROBENIUS_COEFF = Fp.Neg(BigInteger.One);

    // 1/2 in Fp (for sqrt)
    private static readonly BigInteger FP_DIV2 = Fp.Inv(2);

    internal static Fp2Element Create(BigInteger c0, BigInteger c1)
    {
        return new(Fp.Create(c0), Fp.Create(c1));
    }

    internal static bool IsZero(Fp2Element a)
    {
        return Fp.IsZero(a.C0) && Fp.IsZero(a.C1);
    }

    internal static bool Eql(Fp2Element a, Fp2Element b)
    {
        return Fp.Eql(a.C0, b.C0) && Fp.Eql(a.C1, b.C1);
    }

    internal static Fp2Element Neg(Fp2Element a)
    {
        return new(Fp.Neg(a.C0), Fp.Neg(a.C1));
    }

    internal static Fp2Element Add(Fp2Element a, Fp2Element b)
    {
        return new(Fp.Add(a.C0, b.C0), Fp.Add(a.C1, b.C1));
    }

    internal static Fp2Element Sub(Fp2Element a, Fp2Element b)
    {
        return new(Fp.Sub(a.C0, b.C0), Fp.Sub(a.C1, b.C1));
    }

    /// <summary>
    /// (a + bi)(c + di) = (ac - bd) + (ad + bc)i  where i² = -1
    /// Uses Karatsuba-like: ad + bc = (a+b)(c+d) - ac - bd
    /// </summary>
    internal static Fp2Element Mul(Fp2Element a, Fp2Element b)
    {
        BigInteger t1 = Fp.Mul(a.C0, b.C0);
        BigInteger t2 = Fp.Mul(a.C1, b.C1);
        return new(
            Fp.Sub(t1, t2),
            Fp.Sub(Fp.Mul(Fp.Add(a.C0, a.C1), Fp.Add(b.C0, b.C1)), Fp.Add(t1, t2))
        );
    }

    /// <summary>
    /// Multiply by scalar (BigInteger from Fp).
    /// </summary>
    internal static Fp2Element MulScalar(Fp2Element a, BigInteger s)
    {
        return new(Fp.Mul(a.C0, s), Fp.Mul(a.C1, s));
    }

    /// <summary>
    /// Optimized squaring: (a+bi)² = (a+b)(a-b) + 2ab*i
    /// </summary>
    internal static Fp2Element Sqr(Fp2Element a)
    {
        BigInteger sum = Fp.Add(a.C0, a.C1);
        BigInteger diff = Fp.Sub(a.C0, a.C1);
        BigInteger twice_c0 = Fp.Add(a.C0, a.C0);
        return new(Fp.Mul(sum, diff), Fp.Mul(twice_c0, a.C1));
    }

    /// <summary>
    /// Inversion: (a+bi)^-1 = (a-bi)/(a²+b²)
    /// Only one Fp inversion needed.
    /// </summary>
    internal static Fp2Element Inv(Fp2Element a)
    {
        BigInteger factor = Fp.Inv(Fp.Create((a.C0 * a.C0) + (a.C1 * a.C1)));
        return new(Fp.Mul(factor, Fp.Create(a.C0)), Fp.Mul(factor, Fp.Create(-a.C1)));
    }

    internal static Fp2Element Div(Fp2Element a, Fp2Element b)
    {
        return Mul(a, Inv(b));
    }

    internal static Fp2Element Pow(Fp2Element num, BigInteger power)
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

        Fp2Element p = ONE;
        Fp2Element d = num;
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
    /// Square root in Fp2. Generic for quadratic extensions with u²=-1.
    /// </summary>
    internal static Fp2Element Sqrt(Fp2Element num)
    {
        BigInteger c0 = num.C0, c1 = num.C1;

        if (Fp.IsZero(c1))
        {
            // c1 == 0: sqrt is purely real or purely imaginary
            return Fp.Legendre(c0) == 1 ? new(Fp.Sqrt(c0), BigInteger.Zero) : new(BigInteger.Zero, Fp.Sqrt(Fp.Neg(c0)));
        }

        // General case
        // a = sqrt(c0² - (-1)*c1²) = sqrt(c0² + c1²) in Fp
        BigInteger a = Fp.Sqrt(Fp.Sub(Fp.Sqr(c0), Fp.Mul(Fp.Sqr(c1), Fp.Neg(BigInteger.One))));
        BigInteger d = Fp.Mul(Fp.Add(a, c0), FP_DIV2);

        if (Fp.Legendre(d) == -1)
        {
            d = Fp.Sub(d, a);
        }

        BigInteger a0 = Fp.Sqrt(d);
        Fp2Element candidateSqrt = new(a0, Fp.Div(Fp.Mul(c1, FP_DIV2), a0));

        if (!Eql(Sqr(candidateSqrt), num))
        {
            throw new InvalidOperationException("Cannot find square root");
        }

        // Normalize: pick the root with the "larger" representation
        Fp2Element x1 = candidateSqrt;
        Fp2Element x2 = Neg(x1);
        BigInteger re1 = x1.C0, im1 = x1.C1;
        BigInteger re2 = x2.C0, im2 = x2.C1;
        return im1 > im2 || (im1 == im2 && re1 > re2) ? x1 : x2;
    }

    /// <summary>
    /// sgn0_m_eq_2 from RFC 9380: determines "sign" of Fp2 element.
    /// </summary>
    internal static bool IsOdd(Fp2Element x)
    {
        BigInteger x0 = Fp.Create(x.C0);
        BigInteger x1 = Fp.Create(x.C1);
        BigInteger sign_0 = x0 & 1;
        bool zero_0 = x0 == 0;
        BigInteger sign_1 = x1 & 1;
        return (sign_0 | (zero_0 ? sign_1 : 0)) == 1;
    }

    /// <summary>
    /// Multiply by the non-residue (1 + u): used in tower construction.
    /// </summary>
    internal static Fp2Element MulByNonresidue(Fp2Element a)
    {
        return Mul(a, NONRESIDUE);
    }

    /// <summary>
    /// Multiply by b' = 4(1+i) for BLS12-381 twisted curve.
    /// mulByB({c0,c1}) = {4*c0-4*c1, 4*c0+4*c1}
    /// </summary>
    internal static Fp2Element MulByB(Fp2Element a)
    {
        BigInteger t0 = Fp.Mul(a.C0, 4);
        BigInteger t1 = Fp.Mul(a.C1, 4);
        return new(Fp.Sub(t0, t1), Fp.Add(t0, t1));
    }

    /// <summary>
    /// Frobenius map: conjugation when power is odd.
    /// </summary>
    internal static Fp2Element FrobeniusMap(Fp2Element a, int power)
    {
        return power % 2 == 0 ? a : new(a.C0, Fp.Mul(a.C1, FROBENIUS_COEFF));
    }

    /// <summary>
    /// Fp4Square used in cyclotomic squaring.
    /// </summary>
    internal static (Fp2Element First, Fp2Element Second) Fp4Square(Fp2Element a, Fp2Element b)
    {
        Fp2Element a2 = Sqr(a);
        Fp2Element b2 = Sqr(b);
        return (
            Add(MulByNonresidue(b2), a2),
            Sub(Sub(Sqr(Add(a, b)), a2), b2)
        );
    }

    internal static Fp2Element Cmov(Fp2Element a, Fp2Element b, bool c)
    {
        return c ? b : a;
    }

    internal static byte[] ToBytes(Fp2Element a)
    {
        return [.. Fp.ToBytes(a.C0), .. Fp.ToBytes(a.C1)];
    }

    internal static Fp2Element FromBytes(ReadOnlySpan<byte> bytes)
    {
        return new(Fp.FromBytes(bytes[..Fp.BYTES]), Fp.FromBytes(bytes[Fp.BYTES..]));
    }
}
