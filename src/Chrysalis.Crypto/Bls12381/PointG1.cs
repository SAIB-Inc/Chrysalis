using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Affine point on G1 (over Fp).
/// </summary>
internal readonly record struct AffineG1(BigInteger X, BigInteger Y);

/// <summary>
/// Projective point on G1 curve: y² = x³ + 4 over Fp.
/// Uses RCB (Renes-Costello-Batina) complete addition formulas.
/// Ported from noble-curves weierstrass.ts (MIT License, Paul Miller).
/// </summary>
internal sealed class PointG1
{
    // Curve parameters
    private static readonly BigInteger A = BigInteger.Zero;
    private static readonly BigInteger B = 4;
    private static readonly BigInteger B3 = Fp.Mul(B, 3);
    internal static readonly BigInteger N = BigInteger.Parse(
        "73eda753299d7d483339d80809a1d80553bda402fffe5bfeffffffff00000001",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    // BLS_X seed
    private static readonly BigInteger BLS_X = BigInteger.Parse(
        "0d201000000010000",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    // GLV endomorphism beta
    private static readonly BigInteger BETA = BigInteger.Parse(
        "5f19672fdf76ce51ba69c6076a0f77eaddb3a93be6f89688de17d813620a00022e01fffffffefffe",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    internal static readonly PointG1 ZERO = new(BigInteger.Zero, BigInteger.One, BigInteger.Zero);
    internal static readonly PointG1 BASE = new(
        BigInteger.Parse(
            "17f1d3a73197d7942695638c4fa9ac0fc3688c4f9774b905a14e3a3f171bac586c55e83ff97a1aeffb3af00adb22c6bb",
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture),
        BigInteger.Parse(
            "08b3f481e3aaa0f1a09e30ed741d8ae4fcf5e095d5d00af600db18cb2c04b3edd03cc744a2888ae40caa232946c5e7e1",
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture),
        BigInteger.One);

    internal readonly BigInteger X, Y, Z;

    internal PointG1(BigInteger x, BigInteger y, BigInteger z)
    {
        X = x; Y = y; Z = z;
    }

    internal static PointG1 FromAffine(BigInteger x, BigInteger y)
    {
        return Fp.IsZero(x) && Fp.IsZero(y) ? ZERO : new PointG1(x, y, BigInteger.One);
    }

    internal bool Is0()
    {
        return Equals(ZERO);
    }

    internal bool Equals(PointG1 other)
    {
        return Fp.Eql(Fp.Mul(X, other.Z), Fp.Mul(other.X, Z)) &&
        Fp.Eql(Fp.Mul(Y, other.Z), Fp.Mul(other.Y, Z));
    }

    internal PointG1 Negate()
    {
        return new(X, Fp.Neg(Y), Z);
    }

    /// <summary>
    /// RCB exception-free doubling (algorithm 3 from eprint 2015/1060).
    /// Simplified for a=0.
    /// </summary>
    internal PointG1 Double()
    {
        BigInteger X1 = X, Y1 = Y, Z1 = Z;
        BigInteger t0 = Fp.Mul(X1, X1);
        BigInteger t1 = Fp.Mul(Y1, Y1);
        BigInteger t2 = Fp.Mul(Z1, Z1);
        BigInteger t3 = Fp.Mul(X1, Y1);
        t3 = Fp.Add(t3, t3);
        BigInteger Z3 = Fp.Mul(X1, Z1);
        Z3 = Fp.Add(Z3, Z3);
        BigInteger X3 = Fp.Mul(A, Z3);
        BigInteger Y3 = Fp.Mul(B3, t2);
        Y3 = Fp.Add(X3, Y3);
        X3 = Fp.Sub(t1, Y3);
        Y3 = Fp.Add(t1, Y3);
        Y3 = Fp.Mul(X3, Y3);
        X3 = Fp.Mul(t3, X3);
        Z3 = Fp.Mul(B3, Z3);
        t2 = Fp.Mul(A, t2);
        t3 = Fp.Sub(t0, t2);
        t3 = Fp.Mul(A, t3);
        t3 = Fp.Add(t3, Z3);
        Z3 = Fp.Add(t0, t0);
        t0 = Fp.Add(Z3, t0);
        t0 = Fp.Add(t0, t2);
        t0 = Fp.Mul(t0, t3);
        Y3 = Fp.Add(Y3, t0);
        t2 = Fp.Mul(Y1, Z1);
        t2 = Fp.Add(t2, t2);
        t0 = Fp.Mul(t2, t3);
        X3 = Fp.Sub(X3, t0);
        Z3 = Fp.Mul(t2, t1);
        Z3 = Fp.Add(Z3, Z3);
        Z3 = Fp.Add(Z3, Z3);
        return new PointG1(X3, Y3, Z3);
    }

    /// <summary>
    /// RCB exception-free addition (algorithm 1 from eprint 2015/1060).
    /// </summary>
    internal PointG1 Add(PointG1 other)
    {
        BigInteger X1 = X, Y1 = Y, Z1 = Z;
        BigInteger X2 = other.X, Y2 = other.Y, Z2 = other.Z;
        BigInteger t0 = Fp.Mul(X1, X2);
        BigInteger t1 = Fp.Mul(Y1, Y2);
        BigInteger t2 = Fp.Mul(Z1, Z2);
        BigInteger t3 = Fp.Add(X1, Y1);
        BigInteger t4 = Fp.Add(X2, Y2);
        t3 = Fp.Mul(t3, t4);
        t4 = Fp.Add(t0, t1);
        t3 = Fp.Sub(t3, t4);
        t4 = Fp.Add(X1, Z1);
        BigInteger t5 = Fp.Add(X2, Z2);
        t4 = Fp.Mul(t4, t5);
        t5 = Fp.Add(t0, t2);
        t4 = Fp.Sub(t4, t5);
        t5 = Fp.Add(Y1, Z1);
        BigInteger X3 = Fp.Add(Y2, Z2);
        t5 = Fp.Mul(t5, X3);
        X3 = Fp.Add(t1, t2);
        t5 = Fp.Sub(t5, X3);
        BigInteger Z3 = Fp.Mul(A, t4);
        X3 = Fp.Mul(B3, t2);
        Z3 = Fp.Add(X3, Z3);
        X3 = Fp.Sub(t1, Z3);
        Z3 = Fp.Add(t1, Z3);
        BigInteger Y3 = Fp.Mul(X3, Z3);
        t1 = Fp.Add(t0, t0);
        t1 = Fp.Add(t1, t0);
        t2 = Fp.Mul(A, t2);
        t4 = Fp.Mul(B3, t4);
        t1 = Fp.Add(t1, t2);
        t2 = Fp.Sub(t0, t2);
        t2 = Fp.Mul(A, t2);
        t4 = Fp.Add(t4, t2);
        t0 = Fp.Mul(t1, t4);
        Y3 = Fp.Add(Y3, t0);
        t0 = Fp.Mul(t5, t4);
        X3 = Fp.Mul(t3, X3);
        X3 = Fp.Sub(X3, t0);
        t0 = Fp.Mul(t3, t1);
        Z3 = Fp.Mul(t5, Z3);
        Z3 = Fp.Add(Z3, t0);
        return new PointG1(X3, Y3, Z3);
    }

    internal PointG1 Subtract(PointG1 other)
    {
        return Add(other.Negate());
    }

    /// <summary>
    /// Non-constant-time double-and-add multiplication.
    /// Safe for public inputs (Plutus VM operates on public data).
    /// </summary>
    internal PointG1 Multiply(BigInteger scalar)
    {
        if (scalar <= 0 || scalar >= N)
        {
            throw new ArgumentException("invalid scalar");
        }

        PointG1 result = ZERO;
        PointG1 temp = this;
        while (scalar > 0)
        {
            if ((scalar & 1) == 1)
            {
                result = result.Add(temp);
            }

            temp = temp.Double();
            scalar >>= 1;
        }
        return result;
    }

    internal PointG1 MultiplyUnsafe(BigInteger scalar)
    {
        return scalar == 0 || Is0() ? ZERO : scalar == 1 ? this : Multiply(FpUtils.PosMod(scalar, N));
    }

    internal AffineG1 ToAffine()
    {
        if (Is0())
        {
            return new AffineG1(BigInteger.Zero, BigInteger.Zero);
        }

        if (Fp.Eql(Z, BigInteger.One))
        {
            return new AffineG1(X, Y);
        }

        BigInteger zInv = Fp.Inv(Z);
        return new AffineG1(Fp.Mul(X, zInv), Fp.Mul(Y, zInv));
    }

    /// <summary>
    /// Validate point is on curve and in prime-order subgroup.
    /// </summary>
    internal void AssertValidity()
    {
        if (Is0())
        {
            return;
        }

        AffineG1 a = ToAffine();
        // Check on curve: y² = x³ + 4
        BigInteger lhs = Fp.Sqr(a.Y);
        BigInteger rhs = Fp.Add(Fp.Pow(a.X, 3), Fp.Create(B));
        if (!Fp.Eql(lhs, rhs))
        {
            throw new InvalidOperationException("G1 point not on curve");
        }
        // Torsion-free check via GLV endomorphism
        if (!IsTorsionFree())
        {
            throw new InvalidOperationException("G1 point not in prime-order subgroup");
        }
    }

    private bool IsTorsionFree()
    {
        // ψ(P) = (β·x, y) in projective: phi = (β·X, Y, Z)
        PointG1 phi = new(Fp.Mul(X, BETA), Y, Z);
        PointG1 xP = MultiplyUnsafe(BLS_X).Negate();
        PointG1 u2P = xP.MultiplyUnsafe(BLS_X);
        return u2P.Equals(phi);
    }

    /// <summary>
    /// Clear cofactor: x*P + P
    /// </summary>
    internal PointG1 ClearCofactor()
    {
        return MultiplyUnsafe(BLS_X).Add(this);
    }

    /// <summary>
    /// Compress G1 point to 48 bytes (ZCash format).
    /// </summary>
    internal byte[] Compress()
    {
        if (Is0())
        {
            byte[] inf = new byte[48];
            inf[0] = 0xC0; // compressed + infinity flags
            return inf;
        }
        AffineG1 a = ToAffine();
        bool sort = (a.Y * 2 / Fp.P) != 0;
        byte[] bytes = FpUtils.NumberToBytesBE(a.X, 48);
        bytes[0] |= 0x80; // compressed flag
        if (sort)
        {
            bytes[0] |= 0x20; // sort flag
        }

        return bytes;
    }

    /// <summary>
    /// Decompress 48-byte compressed G1 point.
    /// </summary>
    internal static PointG1 Uncompress(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 48)
        {
            throw new ArgumentException("G1 uncompress: expected 48 bytes");
        }

        byte[] value = bytes.ToArray();
        bool compressed = (value[0] & 0x80) != 0;
        bool infinity = (value[0] & 0x40) != 0;
        bool sort = (value[0] & 0x20) != 0;
        value[0] &= 0x1F; // clear top 3 bits

        if (!compressed)
        {
            throw new ArgumentException("G1 uncompress: not compressed");
        }

        BigInteger x = FpUtils.BytesToNumberBE(value);

        if (infinity)
        {
            return x != 0 || sort
                ? throw new ArgumentException("G1 uncompress: invalid infinity point")
                : ZERO;
        }

        // y² = x³ + 4
        BigInteger right = Fp.Add(Fp.Pow(x, 3), Fp.Create(B));
        BigInteger y = Fp.Sqrt(right);
        if ((y * 2 / Fp.P) != (sort ? BigInteger.One : BigInteger.Zero))
        {
            y = Fp.Neg(y);
        }

        PointG1 point = FromAffine(Fp.Create(x), Fp.Create(y));
        point.AssertValidity();
        return point;
    }
}
