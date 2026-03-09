using System.Numerics;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Affine point on G2 (over Fp2).
/// </summary>
internal readonly record struct AffineG2(Fp2Element X, Fp2Element Y);

/// <summary>
/// Projective point on G2 curve: y² = x³ + 4(u+1) over Fp2.
/// Uses RCB (Renes-Costello-Batina) complete addition formulas.
/// Ported from noble-curves weierstrass.ts (MIT License, Paul Miller).
/// </summary>
internal sealed class PointG2
{
    // Curve parameters: a=0, b=4(1+u)
    private static readonly Fp2Element A = Fp2.ZERO;
    private static readonly Fp2Element B = Fp2.Create(4, 4);
    private static readonly Fp2Element B3 = Fp2.MulScalar(B, 3);

    internal static readonly BigInteger N = PointG1.N; // same scalar field order

    // BLS_X seed
    private static readonly BigInteger BLS_X = BigInteger.Parse(
        "0d201000000010000",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    // Psi endomorphism constants
    // PSI_X = (1/(u+1))^((p-1)/3) in Fp2
    // PSI_Y = (1/(u+1))^((p-1)/2) in Fp2
    private static readonly Fp2Element PSI_X = ComputePsiX();
    private static readonly Fp2Element PSI_Y = ComputePsiY();
    // PSI2_X = (1/(u+1))^((p²-1)/3) in Fp2
    private static readonly Fp2Element PSI2_X = ComputePsi2X();

    internal static readonly PointG2 ZERO = new(Fp2.ZERO, Fp2.ONE, Fp2.ZERO);
    internal static readonly PointG2 BASE = new(
        Fp2.Create(
            BigInteger.Parse(
                "024aa2b2f08f0a91260805272dc51051c6e47ad4fa403b02b4510b647ae3d1770bac0326a805bbefd48056c8c121bdb8",
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture),
            BigInteger.Parse(
                "13e02b6052719f607dacd3a088274f65596bd0d09920b61ab5da61bbdc7f5049334cf11213945d57e5ac7d055d042b7e",
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture)),
        Fp2.Create(
            BigInteger.Parse(
                "0ce5d527727d6e118cc9cdc6da2e351aadfd9baa8cbdd3a76d429a695160d12c923ac9cc3baca289e193548608b82801",
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture),
            BigInteger.Parse(
                "0606c4a02ea734cc32acd2b02bc28b99cb3e287e85a763af267492ab572e99ab3f370d275cec1da1aaa9075ff05f79be",
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture)),
        Fp2.ONE);

    internal readonly Fp2Element X, Y, Z;

    internal PointG2(Fp2Element x, Fp2Element y, Fp2Element z)
    {
        X = x; Y = y; Z = z;
    }

    internal static PointG2 FromAffine(Fp2Element x, Fp2Element y) => Fp2.IsZero(x) && Fp2.IsZero(y) ? ZERO : new PointG2(x, y, Fp2.ONE);

    internal bool Is0() => Equals(ZERO);

    internal bool Equals(PointG2 other) =>
        Fp2.Eql(Fp2.Mul(X, other.Z), Fp2.Mul(other.X, Z)) &&
        Fp2.Eql(Fp2.Mul(Y, other.Z), Fp2.Mul(other.Y, Z));

    internal PointG2 Negate() => new(X, Fp2.Neg(Y), Z);

    /// <summary>
    /// RCB exception-free doubling (algorithm 3 from eprint 2015/1060).
    /// Simplified for a=0.
    /// </summary>
    internal PointG2 Double()
    {
        Fp2Element X1 = X, Y1 = Y, Z1 = Z;
        Fp2Element t0 = Fp2.Mul(X1, X1);
        Fp2Element t1 = Fp2.Mul(Y1, Y1);
        Fp2Element t2 = Fp2.Mul(Z1, Z1);
        Fp2Element t3 = Fp2.Mul(X1, Y1);
        t3 = Fp2.Add(t3, t3);
        Fp2Element Z3 = Fp2.Mul(X1, Z1);
        Z3 = Fp2.Add(Z3, Z3);
        Fp2Element X3 = Fp2.Mul(A, Z3);
        Fp2Element Y3 = Fp2.Mul(B3, t2);
        Y3 = Fp2.Add(X3, Y3);
        X3 = Fp2.Sub(t1, Y3);
        Y3 = Fp2.Add(t1, Y3);
        Y3 = Fp2.Mul(X3, Y3);
        X3 = Fp2.Mul(t3, X3);
        Z3 = Fp2.Mul(B3, Z3);
        t2 = Fp2.Mul(A, t2);
        t3 = Fp2.Sub(t0, t2);
        t3 = Fp2.Mul(A, t3);
        t3 = Fp2.Add(t3, Z3);
        Z3 = Fp2.Add(t0, t0);
        t0 = Fp2.Add(Z3, t0);
        t0 = Fp2.Add(t0, t2);
        t0 = Fp2.Mul(t0, t3);
        Y3 = Fp2.Add(Y3, t0);
        t2 = Fp2.Mul(Y1, Z1);
        t2 = Fp2.Add(t2, t2);
        t0 = Fp2.Mul(t2, t3);
        X3 = Fp2.Sub(X3, t0);
        Z3 = Fp2.Mul(t2, t1);
        Z3 = Fp2.Add(Z3, Z3);
        Z3 = Fp2.Add(Z3, Z3);
        return new PointG2(X3, Y3, Z3);
    }

    /// <summary>
    /// RCB exception-free addition (algorithm 1 from eprint 2015/1060).
    /// </summary>
    internal PointG2 Add(PointG2 other)
    {
        Fp2Element X1 = X, Y1 = Y, Z1 = Z;
        Fp2Element X2 = other.X, Y2 = other.Y, Z2 = other.Z;
        Fp2Element t0 = Fp2.Mul(X1, X2);
        Fp2Element t1 = Fp2.Mul(Y1, Y2);
        Fp2Element t2 = Fp2.Mul(Z1, Z2);
        Fp2Element t3 = Fp2.Add(X1, Y1);
        Fp2Element t4 = Fp2.Add(X2, Y2);
        t3 = Fp2.Mul(t3, t4);
        t4 = Fp2.Add(t0, t1);
        t3 = Fp2.Sub(t3, t4);
        t4 = Fp2.Add(X1, Z1);
        Fp2Element t5 = Fp2.Add(X2, Z2);
        t4 = Fp2.Mul(t4, t5);
        t5 = Fp2.Add(t0, t2);
        t4 = Fp2.Sub(t4, t5);
        t5 = Fp2.Add(Y1, Z1);
        Fp2Element X3 = Fp2.Add(Y2, Z2);
        t5 = Fp2.Mul(t5, X3);
        X3 = Fp2.Add(t1, t2);
        t5 = Fp2.Sub(t5, X3);
        Fp2Element Z3 = Fp2.Mul(A, t4);
        X3 = Fp2.Mul(B3, t2);
        Z3 = Fp2.Add(X3, Z3);
        X3 = Fp2.Sub(t1, Z3);
        Z3 = Fp2.Add(t1, Z3);
        Fp2Element Y3 = Fp2.Mul(X3, Z3);
        t1 = Fp2.Add(t0, t0);
        t1 = Fp2.Add(t1, t0);
        t2 = Fp2.Mul(A, t2);
        t4 = Fp2.Mul(B3, t4);
        t1 = Fp2.Add(t1, t2);
        t2 = Fp2.Sub(t0, t2);
        t2 = Fp2.Mul(A, t2);
        t4 = Fp2.Add(t4, t2);
        t0 = Fp2.Mul(t1, t4);
        Y3 = Fp2.Add(Y3, t0);
        t0 = Fp2.Mul(t5, t4);
        X3 = Fp2.Mul(t3, X3);
        X3 = Fp2.Sub(X3, t0);
        t0 = Fp2.Mul(t3, t1);
        Z3 = Fp2.Mul(t5, Z3);
        Z3 = Fp2.Add(Z3, t0);
        return new PointG2(X3, Y3, Z3);
    }

    internal PointG2 Subtract(PointG2 other) => Add(other.Negate());

    /// <summary>
    /// Non-constant-time double-and-add multiplication.
    /// </summary>
    internal PointG2 Multiply(BigInteger scalar)
    {
        if (scalar <= 0 || scalar >= N)
        {
            throw new ArgumentException("invalid scalar");
        }

        PointG2 result = ZERO;
        PointG2 temp = this;
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

    internal PointG2 MultiplyUnsafe(BigInteger scalar)
    {
        return scalar == 0 || Is0() ? ZERO : scalar == 1 ? this : Multiply(FpUtils.PosMod(scalar, N));
    }

    internal AffineG2 ToAffine()
    {
        if (Is0())
        {
            return new AffineG2(Fp2.ZERO, Fp2.ZERO);
        }

        if (Fp2.Eql(Z, Fp2.ONE))
        {
            return new AffineG2(X, Y);
        }

        Fp2Element zInv = Fp2.Inv(Z);
        return new AffineG2(Fp2.Mul(X, zInv), Fp2.Mul(Y, zInv));
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

        AffineG2 a = ToAffine();
        // y² = x³ + 4(1+u)
        Fp2Element lhs = Fp2.Sqr(a.Y);
        Fp2Element rhs = Fp2.Add(Fp2.Pow(a.X, 3), B);
        if (!Fp2.Eql(lhs, rhs))
        {
            throw new InvalidOperationException("G2 point not on curve");
        }

        if (!IsTorsionFree())
        {
            throw new InvalidOperationException("G2 point not in prime-order subgroup");
        }
    }

    private bool IsTorsionFree()
    {
        // ψ(P) == -[x]P
        PointG2 psiP = Psi(this);
        PointG2 xP = MultiplyUnsafe(BLS_X).Negate();
        return xP.Equals(psiP);
    }

    /// <summary>
    /// Psi Frobenius endomorphism: ψ(x, y) = (frobenius(x) * PSI_X, frobenius(y) * PSI_Y)
    /// </summary>
    internal static PointG2 Psi(PointG2 p)
    {
        if (p.Is0())
        {
            return ZERO;
        }

        AffineG2 a = p.ToAffine();
        Fp2Element x2 = Fp2.Mul(Fp2.FrobeniusMap(a.X, 1), PSI_X);
        Fp2Element y2 = Fp2.Mul(Fp2.FrobeniusMap(a.Y, 1), PSI_Y);
        return FromAffine(x2, y2);
    }

    /// <summary>
    /// Psi² endomorphism: ψ²(x, y) = (x * PSI2_X, -y)
    /// </summary>
    internal static PointG2 Psi2(PointG2 p)
    {
        if (p.Is0())
        {
            return ZERO;
        }

        AffineG2 a = p.ToAffine();
        return FromAffine(Fp2.Mul(a.X, PSI2_X), Fp2.Neg(a.Y));
    }

    /// <summary>
    /// Clear cofactor using RFC 9380 method for BLS12-381 G2.
    /// </summary>
    internal PointG2 ClearCofactor()
    {
        BigInteger x = BLS_X;
        PointG2 t1 = MultiplyUnsafe(x).Negate();      // [-x]P
        PointG2 t2 = Psi(this);                        // Ψ(P)
        PointG2 t3 = Double();                          // 2P
        t3 = Psi2(t3);                                 // Ψ²(2P)
        t3 = t3.Subtract(t2);                           // Ψ²(2P) - Ψ(P)
        t2 = t1.Add(t2);                                // [-x]P + Ψ(P)
        t2 = t2.MultiplyUnsafe(x).Negate();             // [x²]P - [x]Ψ(P)
        t3 = t3.Add(t2);                                // Ψ²(2P) - Ψ(P) + [x²]P - [x]Ψ(P)
        t3 = t3.Subtract(t1);                           // + [x]P
        PointG2 Q = t3.Subtract(this);                  // - P
        return Q;
    }

    /// <summary>
    /// Compress G2 point to 96 bytes (ZCash format).
    /// Layout: [flags | x.c1 (48 bytes)] [x.c0 (48 bytes)]
    /// </summary>
    internal byte[] Compress()
    {
        if (Is0())
        {
            byte[] inf = new byte[96];
            inf[0] = 0xC0; // compressed + infinity flags
            return inf;
        }
        AffineG2 a = ToAffine();
        bool sort = a.Y.C1 == BigInteger.Zero
            ? (a.Y.C0 * 2 / Fp.P) != 0
            : (a.Y.C1 * 2 / Fp.P) != 0;
        byte[] c1Bytes = FpUtils.NumberToBytesBE(a.X.C1, 48);
        byte[] c0Bytes = FpUtils.NumberToBytesBE(a.X.C0, 48);
        c1Bytes[0] |= 0x80; // compressed flag
        if (sort)
        {
            c1Bytes[0] |= 0x20; // sort flag
        }

        byte[] result = new byte[96];
        c1Bytes.CopyTo(result.AsSpan());
        c0Bytes.CopyTo(result.AsSpan(48));
        return result;
    }

    /// <summary>
    /// Decompress 96-byte compressed G2 point.
    /// </summary>
    internal static PointG2 Uncompress(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 96)
        {
            throw new ArgumentException("G2 uncompress: expected 96 bytes");
        }

        byte[] value = bytes.ToArray();
        bool compressed = (value[0] & 0x80) != 0;
        bool infinity = (value[0] & 0x40) != 0;
        bool sort = (value[0] & 0x20) != 0;
        value[0] &= 0x1F; // clear top 3 bits

        if (!compressed)
        {
            throw new ArgumentException("G2 uncompress: not compressed");
        }

        BigInteger x_1 = FpUtils.BytesToNumberBE(value.AsSpan(0, 48));
        BigInteger x_0 = FpUtils.BytesToNumberBE(value.AsSpan(48, 48));

        if (infinity)
        {
            return x_0 != 0 || x_1 != 0 || sort
                ? throw new ArgumentException("G2 uncompress: invalid infinity point")
                : ZERO;
        }

        Fp2Element x = Fp2.Create(Fp.Create(x_0), Fp.Create(x_1));

        // y² = x³ + 4(1+u)
        Fp2Element right = Fp2.Add(Fp2.Pow(x, 3), B);
        Fp2Element y = Fp2.Sqrt(right);

        BigInteger Y_bit = y.C1 == BigInteger.Zero
            ? (y.C0 * 2 / Fp.P)
            : (y.C1 * 2 / Fp.P) != BigInteger.Zero ? BigInteger.One : BigInteger.Zero;
        if (sort && Y_bit > 0)
        {
            // sort flag matches — keep y
        }
        else if (!sort && Y_bit == 0)
        {
            // no sort flag matches — keep y
        }
        else
        {
            y = Fp2.Neg(y);
        }

        PointG2 point = FromAffine(x, y);
        point.AssertValidity();
        return point;
    }

    // Compute PSI_X = (1/(u+1))^((p-1)/3)
    private static Fp2Element ComputePsiX()
    {
        Fp2Element inv_nr = Fp2.Inv(Fp2.NONRESIDUE); // 1/(1+u)
        BigInteger exp = (Fp.P - 1) / 3;
        return Fp2.Pow(inv_nr, exp);
    }

    // Compute PSI_Y = (1/(u+1))^((p-1)/2)
    private static Fp2Element ComputePsiY()
    {
        Fp2Element inv_nr = Fp2.Inv(Fp2.NONRESIDUE);
        BigInteger exp = (Fp.P - 1) / 2;
        return Fp2.Pow(inv_nr, exp);
    }

    // Compute PSI2_X = (1/(u+1))^((p²-1)/3)
    private static Fp2Element ComputePsi2X()
    {
        Fp2Element inv_nr = Fp2.Inv(Fp2.NONRESIDUE);
        BigInteger exp = ((Fp.P * Fp.P) - 1) / 3;
        return Fp2.Pow(inv_nr, exp);
    }
}
