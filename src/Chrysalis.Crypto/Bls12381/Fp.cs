using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// BLS12-381 base field Fp arithmetic.
/// p = 0x1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaaab
/// Ported from noble-curves (MIT License, Paul Miller).
/// </summary>
internal static class Fp
{
    internal static readonly BigInteger P = BigInteger.Parse(
        "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaaab",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    internal const int BYTES = 48;

    // (P + 1) / 4 — used for sqrt since P ≡ 3 mod 4
    private static readonly BigInteger P1Div4 = (P + 1) >> 2;

    // (P - 1) / 2 — used for Legendre symbol
    private static readonly BigInteger P1Div2 = (P - 1) >> 1;

    internal static BigInteger Mod(BigInteger a)
    {
        BigInteger r = a % P;
        return r >= 0 ? r : r + P;
    }

    internal static BigInteger Add(BigInteger a, BigInteger b) => Mod(a + b);

    internal static BigInteger Sub(BigInteger a, BigInteger b) => Mod(a - b);

    internal static BigInteger Mul(BigInteger a, BigInteger b) => Mod(a * b);

    internal static BigInteger Sqr(BigInteger a) => Mod(a * a);

    internal static BigInteger Neg(BigInteger a) => Mod(-a);

    internal static BigInteger Inv(BigInteger a) => a == 0 ? throw new InvalidOperationException("invert: expected non-zero number") : Invert(a, P);

    internal static BigInteger Pow(BigInteger num, BigInteger power) => BigInteger.ModPow(FpUtils.PosMod(num, P), power, P);

    internal static BigInteger Sqrt(BigInteger n)
    {
        // P ≡ 3 mod 4, so sqrt(n) = n^((P+1)/4)
        BigInteger root = BigInteger.ModPow(FpUtils.PosMod(n, P), P1Div4, P);
        return Mod(root * root) != Mod(n) ? throw new InvalidOperationException("Cannot find square root") : root;
    }

    internal static BigInteger Div(BigInteger a, BigInteger b) => Mul(a, Inv(b));

    internal static bool IsZero(BigInteger a) => FpUtils.PosMod(a, P) == 0;

    internal static bool Eql(BigInteger a, BigInteger b) => FpUtils.PosMod(a, P) == FpUtils.PosMod(b, P);

    internal static bool IsOdd(BigInteger a) => (FpUtils.PosMod(a, P) & 1) == 1;

    internal static BigInteger Create(BigInteger n) => FpUtils.PosMod(n, P);

    /// <summary>
    /// Legendre symbol: returns 1 (QR), -1 (QNR), or 0
    /// </summary>
    internal static int Legendre(BigInteger n)
    {
        BigInteger powered = BigInteger.ModPow(FpUtils.PosMod(n, P), P1Div2, P);
        if (powered == 1)
        {
            return 1;
        }

        if (powered == 0)
        {
            return 0;
        }

        return -1; // powered == P - 1
    }

    internal static byte[] ToBytes(BigInteger n) => FpUtils.NumberToBytesBE(FpUtils.PosMod(n, P), BYTES);

    internal static BigInteger FromBytes(ReadOnlySpan<byte> bytes) => FpUtils.BytesToNumberBE(bytes);

    /// <summary>
    /// Extended Euclidean GCD inversion.
    /// </summary>
    private static BigInteger Invert(BigInteger number, BigInteger modulo)
    {
        BigInteger a = FpUtils.PosMod(number, modulo);
        BigInteger b = modulo;
        BigInteger x = 0, u = 1;
        while (a != 0)
        {
            BigInteger q = BigInteger.DivRem(b, a, out BigInteger r);
            BigInteger m = x - (u * q);
            b = a;
            a = r;
            x = u;
            u = m;
        }
        return b != 1 ? throw new InvalidOperationException("invert: does not exist") : FpUtils.PosMod(x, modulo);
    }
}
