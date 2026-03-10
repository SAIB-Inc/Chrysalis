using System.Numerics;
using System.Security.Cryptography;

namespace Chrysalis.Crypto.Bls12381;

/// <summary>
/// Hash-to-curve implementation for BLS12-381 (RFC 9380).
/// Supports hash_to_field, expand_message_xmd, SWU map, and isogeny maps.
/// Ported from noble-curves hash-to-curve.ts + bls12-381.ts (MIT License, Paul Miller).
/// </summary>
internal static class HashToCurve
{
    // Default DST for BLS signatures
    private static readonly byte[] DEFAULT_DST_G1 =
        "BLS_SIG_BLS12381G1_XMD:SHA-256_SSWU_RO_NUL_"u8.ToArray();
    private static readonly byte[] DEFAULT_DST_G2 =
        "BLS_SIG_BLS12381G2_XMD:SHA-256_SSWU_RO_NUL_"u8.ToArray();

    // Security parameter
    private const int K = 128;

    // SHA-256 parameters
    private const int HASH_OUTPUT_LEN = 32; // b_in_bytes
    private const int HASH_BLOCK_LEN = 64;  // r_in_bytes

    // SWU parameters for G1 (11-isogeny curve)
    private static readonly BigInteger SWU_A_G1 = Fp.Create(BigInteger.Parse(
        "144698a3b8e9433d693a02c96d4982b0ea985383ee66a8d8e8981aefd881ac98936f8da0e0f97f5cf428082d584c1d",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture));
    private static readonly BigInteger SWU_B_G1 = Fp.Create(BigInteger.Parse(
        "12e2908d11688030018b12e8753eee3b2016c1f0f24f4070a0b9c14fcef35ef55a23215a316ceaa5d1cc48e98e172be0",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture));
    private static readonly BigInteger SWU_Z_G1 = Fp.Create(11);

    // SWU parameters for G2 (3-isogeny curve)
    private static readonly Fp2Element SWU_A_G2 = Fp2.Create(BigInteger.Zero, Fp.Create(240));
    private static readonly Fp2Element SWU_B_G2 = Fp2.Create(Fp.Create(1012), Fp.Create(1012));
    private static readonly Fp2Element SWU_Z_G2 = Fp2.Create(Fp.Create(Fp.P - 2), Fp.Create(Fp.P - 1));

    // Precomputed constants for SWU sqrt_ratio (q ≡ 3 mod 4)
    // c1 = (q - 3) / 4 for Fp
    private static readonly BigInteger SWU_C1_FP = (Fp.P - 3) / 4;
    // c2 = sqrt(-Z) for Fp
    private static readonly BigInteger SWU_C2_FP = Fp.Sqrt(Fp.Neg(SWU_Z_G1));

    // General Tonelli-Shanks constants for Fp2 sqrt_ratio (q = p², q ≡ 1 mod 4)
    // Factor q - 1 = 2^S * Q where Q is odd
    private static readonly BigInteger FP2_ORDER = Fp.P * Fp.P;
    private static readonly int TS_S = ComputeS(FP2_ORDER - 1);
    private static readonly BigInteger TS_Q = (FP2_ORDER - 1) >> TS_S;
    private static readonly BigInteger TS_C3 = (TS_Q - 1) / 2;
    private static readonly BigInteger TS_C4 = (BigInteger.One << TS_S) - 1;
    private static readonly BigInteger TS_C5 = BigInteger.One << (TS_S - 1);
    private static readonly Fp2Element TS_C6 = Fp2.Pow(SWU_Z_G2, TS_Q);
    private static readonly Fp2Element TS_C7 = Fp2.Pow(SWU_Z_G2, (TS_Q + 1) / 2);

    private static int ComputeS(BigInteger qm1)
    {
        int s = 0;
        while ((qm1 & 1) == 0)
        {
            qm1 >>= 1;
            s++;
        }
        return s;
    }

    #region expand_message_xmd

    /// <summary>
    /// expand_message_xmd from RFC 9380 Section 5.3.1.
    /// Uses SHA-256 as the hash function.
    /// </summary>
    internal static byte[] ExpandMessageXmd(byte[] msg, byte[] DST, int lenInBytes)
    {
        // If DST > 255 bytes, hash it
        if (DST.Length > 255)
        {
            byte[] prefix = "H2C-OVERSIZE-DST-"u8.ToArray();
            DST = SHA256.HashData([.. prefix, .. DST]);
        }

        int ell = (lenInBytes + HASH_OUTPUT_LEN - 1) / HASH_OUTPUT_LEN;
        if (lenInBytes > 65535 || ell > 255)
        {
            throw new ArgumentException("expand_message_xmd: invalid lenInBytes");
        }

        byte[] DST_prime = [.. DST, (byte)DST.Length];
        byte[] Z_pad = new byte[HASH_BLOCK_LEN];
        byte[] l_i_b_str = [(byte)(lenInBytes >> 8), (byte)(lenInBytes & 0xFF)];

        byte[] b_0 = SHA256.HashData([.. Z_pad, .. msg, .. l_i_b_str, 0, .. DST_prime]);

        byte[][] b = new byte[ell + 1][];
        b[0] = SHA256.HashData([.. b_0, 1, .. DST_prime]);

        for (int i = 1; i < ell; i++)
        {
            byte[] xored = Xor(b_0, b[i - 1]);
            b[i] = SHA256.HashData([.. xored, (byte)(i + 1), .. DST_prime]);
        }

        // Concatenate all b blocks
        byte[] result = new byte[lenInBytes];
        int offset = 0;
        for (int i = 0; i < ell && offset < lenInBytes; i++)
        {
            int toCopy = Math.Min(b[i].Length, lenInBytes - offset);
            Array.Copy(b[i], 0, result, offset, toCopy);
            offset += toCopy;
        }
        return result;
    }

    private static byte[] Xor(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }

        return result;
    }

    #endregion

    #region hash_to_field

    /// <summary>
    /// hash_to_field from RFC 9380 Section 5.2.
    /// For G1 (m=1): returns array of BigInteger[count][1]
    /// For G2 (m=2): returns array of BigInteger[count][2]
    /// </summary>
    internal static BigInteger[][] HashToField(byte[] msg, int count, int m, byte[] DST)
    {
        int L = (381 + K + 7) / 8; // ceil((log2(p) + k) / 8) = ceil(509/8) = 64
        int lenInBytes = count * m * L;

        byte[] prb = ExpandMessageXmd(msg, DST, lenInBytes);

        BigInteger[][] u = new BigInteger[count][];
        for (int i = 0; i < count; i++)
        {
            u[i] = new BigInteger[m];
            for (int j = 0; j < m; j++)
            {
                int elmOffset = L * (j + (i * m));
                byte[] tv = new byte[L];
                Array.Copy(prb, elmOffset, tv, 0, L);
                u[i][j] = FpUtils.PosMod(FpUtils.BytesToNumberBE(tv), Fp.P);
            }
        }
        return u;
    }

    #endregion

    #region SWU Map (Simplified SWU for AB != 0)

    /// <summary>
    /// sqrt_ratio for Fp (q ≡ 3 mod 4 optimized version).
    /// Returns (isValid, y) where y² * v == u if isValid.
    /// </summary>
    private static (bool isValid, BigInteger value) SqrtRatioFp(BigInteger u, BigInteger v)
    {
        BigInteger tv1 = Fp.Sqr(v);
        BigInteger tv2 = Fp.Mul(u, v);
        tv1 = Fp.Mul(tv1, tv2);
        BigInteger y1 = Fp.Pow(tv1, SWU_C1_FP);
        y1 = Fp.Mul(y1, tv2);
        BigInteger y2 = Fp.Mul(y1, SWU_C2_FP);
        BigInteger tv3 = Fp.Mul(Fp.Sqr(y1), v);
        bool isQR = Fp.Eql(tv3, u);
        return (isQR, isQR ? y1 : y2);
    }

    /// <summary>
    /// sqrt_ratio for Fp2 using general Tonelli-Shanks (q = p² ≡ 1 mod 4).
    /// Ported from noble-curves SWUFpSqrtRatio (MIT License, Paul Miller).
    /// </summary>
    private static (bool isValid, Fp2Element value) SqrtRatioFp2(Fp2Element u, Fp2Element v)
    {
        Fp2Element tv1 = TS_C6;                           // 1. tv1 = c6 = Z^Q
        Fp2Element tv2 = Fp2.Pow(v, TS_C4);               // 2. tv2 = v^c4
        Fp2Element tv3 = Fp2.Sqr(tv2);                    // 3. tv3 = tv2^2
        tv3 = Fp2.Mul(tv3, v);                            // 4. tv3 = tv3 * v
        Fp2Element tv5 = Fp2.Mul(u, tv3);                  // 5. tv5 = u * tv3
        tv5 = Fp2.Pow(tv5, TS_C3);                        // 6. tv5 = tv5^c3
        tv5 = Fp2.Mul(tv5, tv2);                           // 7. tv5 = tv5 * tv2
        tv2 = Fp2.Mul(tv5, v);                             // 8. tv2 = tv5 * v
        tv3 = Fp2.Mul(tv5, u);                             // 9. tv3 = tv5 * u
        Fp2Element tv4 = Fp2.Mul(tv3, tv2);                // 10. tv4 = tv3 * tv2
        tv5 = Fp2.Pow(tv4, TS_C5);                        // 11. tv5 = tv4^c5
        bool isQR = Fp2.Eql(tv5, Fp2.ONE);                // 12. isQR = tv5 == 1
        tv2 = Fp2.Mul(tv3, TS_C7);                        // 13. tv2 = tv3 * c7
        tv5 = Fp2.Mul(tv4, tv1);                           // 14. tv5 = tv4 * tv1
        tv3 = Fp2.Cmov(tv2, tv3, isQR);                   // 15. tv3 = CMOV(tv2, tv3, isQR)
        tv4 = Fp2.Cmov(tv5, tv4, isQR);                   // 16. tv4 = CMOV(tv5, tv4, isQR)
        // 17. for i in (c1, c1 - 1, ..., 2):
        for (int i = TS_S; i > 1; i--)
        {
            int e = i - 2;                                 // 18. tv5 = i - 2
            BigInteger pow2e = BigInteger.One << e;         // 19. tv5 = 2^tv5
            Fp2Element tvv5 = Fp2.Pow(tv4, pow2e);         // 20. tv5 = tv4^tv5
            bool e1 = Fp2.Eql(tvv5, Fp2.ONE);             // 21. e1 = tv5 == 1
            tv2 = Fp2.Mul(tv3, tv1);                       // 22. tv2 = tv3 * tv1
            tv1 = Fp2.Mul(tv1, tv1);                       // 23. tv1 = tv1 * tv1
            tvv5 = Fp2.Mul(tv4, tv1);                      // 24. tv5 = tv4 * tv1
            tv3 = Fp2.Cmov(tv2, tv3, e1);                 // 25. tv3 = CMOV(tv2, tv3, e1)
            tv4 = Fp2.Cmov(tvv5, tv4, e1);                // 26. tv4 = CMOV(tv5, tv4, e1)
        }
        return (isQR, tv3);
    }

    /// <summary>
    /// Simplified SWU map for Fp (RFC 9380 Section 6.6.2).
    /// Maps a field element u to a point (x, y) on the isogeny curve.
    /// </summary>
    private static (BigInteger x, BigInteger y) MapToCurveSWU_G1(BigInteger u)
    {
        BigInteger A = SWU_A_G1, B_swu = SWU_B_G1, Z = SWU_Z_G1;

        BigInteger tv1 = Fp.Sqr(u);
        tv1 = Fp.Mul(tv1, Z);
        BigInteger tv2 = Fp.Sqr(tv1);
        tv2 = Fp.Add(tv2, tv1);
        BigInteger tv3 = Fp.Add(tv2, Fp.Create(1));
        tv3 = Fp.Mul(tv3, B_swu);
        BigInteger tv4 = Fp.Eql(tv2, BigInteger.Zero) ? Z : Fp.Neg(tv2);
        tv4 = Fp.Mul(tv4, A);
        tv2 = Fp.Sqr(tv3);
        BigInteger tv6 = Fp.Sqr(tv4);
        BigInteger tv5 = Fp.Mul(tv6, A);
        tv2 = Fp.Add(tv2, tv5);
        tv2 = Fp.Mul(tv2, tv3);
        tv6 = Fp.Mul(tv6, tv4);
        tv5 = Fp.Mul(tv6, B_swu);
        tv2 = Fp.Add(tv2, tv5);
        BigInteger x = Fp.Mul(tv1, tv3);
        (bool isValid, BigInteger value) = SqrtRatioFp(tv2, tv6);
        BigInteger y = Fp.Mul(tv1, u);
        y = Fp.Mul(y, value);
        x = isValid ? tv3 : x;
        y = isValid ? value : y;
        bool e1 = Fp.IsOdd(u) == Fp.IsOdd(y);
        y = e1 ? y : Fp.Neg(y);
        BigInteger tv4_inv = Fp.Inv(tv4);
        x = Fp.Mul(x, tv4_inv);
        return (x, y);
    }

    /// <summary>
    /// Simplified SWU map for Fp2 (RFC 9380 Section 6.6.2).
    /// Maps an Fp2 element u to a point (x, y) on the isogeny curve.
    /// </summary>
    private static (Fp2Element x, Fp2Element y) MapToCurveSWU_G2(Fp2Element u)
    {
        Fp2Element A = SWU_A_G2, B_swu = SWU_B_G2, Z = SWU_Z_G2;

        Fp2Element tv1 = Fp2.Sqr(u);
        tv1 = Fp2.Mul(tv1, Z);
        Fp2Element tv2 = Fp2.Sqr(tv1);
        tv2 = Fp2.Add(tv2, tv1);
        Fp2Element tv3 = Fp2.Add(tv2, Fp2.ONE);
        tv3 = Fp2.Mul(tv3, B_swu);
        Fp2Element tv4 = Fp2.Eql(tv2, Fp2.ZERO) ? Z : Fp2.Neg(tv2);
        tv4 = Fp2.Mul(tv4, A);
        tv2 = Fp2.Sqr(tv3);
        Fp2Element tv6 = Fp2.Sqr(tv4);
        Fp2Element tv5 = Fp2.Mul(tv6, A);
        tv2 = Fp2.Add(tv2, tv5);
        tv2 = Fp2.Mul(tv2, tv3);
        tv6 = Fp2.Mul(tv6, tv4);
        tv5 = Fp2.Mul(tv6, B_swu);
        tv2 = Fp2.Add(tv2, tv5);
        Fp2Element x = Fp2.Mul(tv1, tv3);
        (bool isValid, Fp2Element value) = SqrtRatioFp2(tv2, tv6);
        Fp2Element y = Fp2.Mul(tv1, u);
        y = Fp2.Mul(y, value);
        x = isValid ? tv3 : x;
        y = isValid ? value : y;
        bool e1 = Fp2.IsOdd(u) == Fp2.IsOdd(y);
        y = e1 ? y : Fp2.Neg(y);
        Fp2Element tv4_inv = Fp2.Inv(tv4);
        x = Fp2.Mul(x, tv4_inv);
        return (x, y);
    }

    #endregion

    #region Isogeny Maps

    /// <summary>
    /// Apply isogeny map: evaluates rational functions xNum/xDen and y*yNum/yDen.
    /// Coefficients are in reverse order (high degree first) for Horner's method.
    /// </summary>
    private static (BigInteger x, BigInteger y) ApplyIsogenyMapG1(BigInteger x, BigInteger y)
    {
        BigInteger[][] map = IsogenyCoeffsG1();
        BigInteger[] xn = map[0], xd = map[1], yn = map[2], yd = map[3];

        BigInteger xNum = EvalPoly(xn, x);
        BigInteger xDen = EvalPoly(xd, x);
        BigInteger yNum = EvalPoly(yn, x);
        BigInteger yDen = EvalPoly(yd, x);

        BigInteger xDenInv = Fp.Inv(xDen);
        BigInteger yDenInv = Fp.Inv(yDen);

        return (Fp.Mul(xNum, xDenInv), Fp.Mul(y, Fp.Mul(yNum, yDenInv)));
    }

    private static BigInteger EvalPoly(BigInteger[] coeffs, BigInteger x)
    {
        // Coefficients are low-to-high (constant first), reverse for Horner
        BigInteger acc = coeffs[^1];
        for (int i = coeffs.Length - 2; i >= 0; i--)
        {
            acc = Fp.Add(Fp.Mul(acc, x), coeffs[i]);
        }

        return acc;
    }

    /// <summary>
    /// Apply isogeny map for G2 over Fp2.
    /// </summary>
    private static (Fp2Element x, Fp2Element y) ApplyIsogenyMapG2(Fp2Element x, Fp2Element y)
    {
        Fp2Element[][] map = IsogenyCoeffsG2();
        Fp2Element[] xn = map[0], xd = map[1], yn = map[2], yd = map[3];

        Fp2Element xNum = EvalPolyFp2(xn, x);
        Fp2Element xDen = EvalPolyFp2(xd, x);
        Fp2Element yNum = EvalPolyFp2(yn, x);
        Fp2Element yDen = EvalPolyFp2(yd, x);

        Fp2Element xDenInv = Fp2.Inv(xDen);
        Fp2Element yDenInv = Fp2.Inv(yDen);

        return (Fp2.Mul(xNum, xDenInv), Fp2.Mul(y, Fp2.Mul(yNum, yDenInv)));
    }

    private static Fp2Element EvalPolyFp2(Fp2Element[] coeffs, Fp2Element x)
    {
        Fp2Element acc = coeffs[^1];
        for (int i = coeffs.Length - 2; i >= 0; i--)
        {
            acc = Fp2.Add(Fp2.Mul(acc, x), coeffs[i]);
        }

        return acc;
    }

    #endregion

    #region Hash to G1

    /// <summary>
    /// Hash arbitrary bytes to a point on G1 (hash_to_curve).
    /// </summary>
    internal static PointG1 HashToG1(byte[] msg, byte[]? dst = null)
    {
        dst ??= DEFAULT_DST_G1;
        BigInteger[][] u = HashToField(msg, 2, 1, dst);

        (BigInteger x0, BigInteger y0) = MapToCurveSWU_G1(Fp.Create(u[0][0]));
        (BigInteger ix0, BigInteger iy0) = ApplyIsogenyMapG1(x0, y0);
        PointG1 p0 = PointG1.FromAffine(ix0, iy0);

        (BigInteger x1, BigInteger y1) = MapToCurveSWU_G1(Fp.Create(u[1][0]));
        (BigInteger ix1, BigInteger iy1) = ApplyIsogenyMapG1(x1, y1);
        PointG1 p1 = PointG1.FromAffine(ix1, iy1);

        PointG1 result = p0.Add(p1).ClearCofactor();
        if (!result.Is0())
        {
            result.AssertValidity();
        }

        return result;
    }

    /// <summary>
    /// Encode arbitrary bytes to a point on G1 (encode_to_curve).
    /// </summary>
    internal static PointG1 EncodeToG1(byte[] msg, byte[]? dst = null)
    {
        dst ??= DEFAULT_DST_G1;
        BigInteger[][] u = HashToField(msg, 1, 1, dst);

        (BigInteger x0, BigInteger y0) = MapToCurveSWU_G1(Fp.Create(u[0][0]));
        (BigInteger ix0, BigInteger iy0) = ApplyIsogenyMapG1(x0, y0);
        PointG1 p0 = PointG1.FromAffine(ix0, iy0);

        PointG1 result = p0.ClearCofactor();
        if (!result.Is0())
        {
            result.AssertValidity();
        }

        return result;
    }

    #endregion

    #region Hash to G2

    /// <summary>
    /// Hash arbitrary bytes to a point on G2 (hash_to_curve).
    /// </summary>
    internal static PointG2 HashToG2(byte[] msg, byte[]? dst = null)
    {
        dst ??= DEFAULT_DST_G2;
        BigInteger[][] u = HashToField(msg, 2, 2, dst);

        Fp2Element u0 = Fp2.Create(Fp.Create(u[0][0]), Fp.Create(u[0][1]));
        (Fp2Element x0, Fp2Element y0) = MapToCurveSWU_G2(u0);
        (Fp2Element ix0, Fp2Element iy0) = ApplyIsogenyMapG2(x0, y0);
        PointG2 p0 = PointG2.FromAffine(ix0, iy0);

        Fp2Element u1 = Fp2.Create(Fp.Create(u[1][0]), Fp.Create(u[1][1]));
        (Fp2Element x1, Fp2Element y1) = MapToCurveSWU_G2(u1);
        (Fp2Element ix1, Fp2Element iy1) = ApplyIsogenyMapG2(x1, y1);
        PointG2 p1 = PointG2.FromAffine(ix1, iy1);

        PointG2 result = p0.Add(p1).ClearCofactor();
        if (!result.Is0())
        {
            result.AssertValidity();
        }

        return result;
    }

    /// <summary>
    /// Encode arbitrary bytes to a point on G2 (encode_to_curve).
    /// </summary>
    internal static PointG2 EncodeToG2(byte[] msg, byte[]? dst = null)
    {
        dst ??= DEFAULT_DST_G2;
        BigInteger[][] u = HashToField(msg, 1, 2, dst);

        Fp2Element u0 = Fp2.Create(Fp.Create(u[0][0]), Fp.Create(u[0][1]));
        (Fp2Element x0, Fp2Element y0) = MapToCurveSWU_G2(u0);
        (Fp2Element ix0, Fp2Element iy0) = ApplyIsogenyMapG2(x0, y0);
        PointG2 p0 = PointG2.FromAffine(ix0, iy0);

        PointG2 result = p0.ClearCofactor();
        if (!result.Is0())
        {
            result.AssertValidity();
        }

        return result;
    }

    #endregion

    #region Isogeny Map Coefficients

    // 11-isogeny map coefficients for G1 (Fp)
    // Coefficients stored in reverse order for Horner evaluation
    private static BigInteger[][] IsogenyCoeffsG1()
    {
        static BigInteger H(string hex)
        {
            return BigInteger.Parse(
            hex, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        }

        BigInteger[] xNum = [
            H("11a05f2b1e833340b809101dd99815856b303e88a2d7005ff2627b56cdb4e2c85610c2d5f2e62d6eaeac1662734649b7"),
            H("17294ed3e943ab2f0588bab22147a81c7c17e75b2f6a8417f565e33c70d1e86b4838f2a6f318c356e834eef1b3cb83bb"),
            H("0d54005db97678ec1d1048c5d10a9a1bce032473295983e56878e501ec68e25c958c3e3d2a09729fe0179f9dac9edcb0"),
            H("1778e7166fcc6db74e0609d307e55412d7f5e4656a8dbf25f1b33289f1b330835336e25ce3107193c5b388641d9b6861"),
            H("0e99726a3199f4436642b4b3e4118e5499db995a1257fb3f086eeb65982fac18985a286f301e77c451154ce9ac8895d9"),
            H("1630c3250d7313ff01d1201bf7a74ab5db3cb17dd952799b9ed3ab9097e68f90a0870d2dcae73d19cd13c1c66f652983"),
            H("0d6ed6553fe44d296a3726c38ae652bfb11586264f0f8ce19008e218f9c86b2a8da25128c1052ecaddd7f225a139ed84"),
            H("17b81e7701abdbe2e8743884d1117e53356de5ab275b4db1a682c62ef0f2753339b7c8f8c8f475af9ccb5618e3f0c88e"),
            H("080d3cf1f9a78fc47b90b33563be990dc43b756ce79f5574a2c596c928c5d1de4fa295f296b74e956d71986a8497e317"),
            H("169b1f8e1bcfa7c42e0c37515d138f22dd2ecb803a0c5c99676314baf4bb1b7fa3190b2edc0327797f241067be390c9e"),
            H("10321da079ce07e272d8ec09d2565b0dfa7dccdde6787f96d50af36003b14866f69b771f8c285decca67df3f1605fb7b"),
            H("06e08c248e260e70bd1e962381edee3d31d79d7e22c837bc23c0bf1bc24c6b68c24b1b80b64d391fa9c8ba2e8ba2d229"),
        ];

        BigInteger[] xDen = [
            H("08ca8d548cff19ae18b2e62f4bd3fa6f01d5ef4ba35b48ba9c9588617fc8ac62b558d681be343df8993cf9fa40d21b1c"),
            H("12561a5deb559c4348b4711298e536367041e8ca0cf0800c0126c2588c48bf5713daa8846cb026e9e5c8276ec82b3bff"),
            H("0b2962fe57a3225e8137e629bff2991f6f89416f5a718cd1fca64e00b11aceacd6a3d0967c94fedcfcc239ba5cb83e19"),
            H("03425581a58ae2fec83aafef7c40eb545b08243f16b1655154cca8abc28d6fd04976d5243eecf5c4130de8938dc62cd8"),
            H("13a8e162022914a80a6f1d5f43e7a07dffdfc759a12062bb8d6b44e833b306da9bd29ba81f35781d539d395b3532a21e"),
            H("0e7355f8e4e667b955390f7f0506c6e9395735e9ce9cad4d0a43bcef24b8982f7400d24bc4228f11c02df9a29f6304a5"),
            H("0772caacf16936190f3e0c63e0596721570f5799af53a1894e2e073062aede9cea73b3538f0de06cec2574496ee84a3a"),
            H("14a7ac2a9d64a8b230b3f5b074cf01996e7f63c21bca68a81996e1cdf9822c580fa5b9489d11e2d311f7d99bbdcc5a5e"),
            H("0a10ecf6ada54f825e920b3dafc7a3cce07f8d1d7161366b74100da67f39883503826692abba43704776ec3a79a1d641"),
            H("095fc13ab9e92ad4476d6e3eb3a56680f682b4ee96f7d03776df533978f31c1593174e4b4b7865002d6384d168ecdd0a"),
            BigInteger.One,
        ];

        BigInteger[] yNum = [
            H("090d97c81ba24ee0259d1f094980dcfa11ad138e48a869522b52af6c956543d3cd0c7aee9b3ba3c2be9845719707bb33"),
            H("134996a104ee5811d51036d776fb46831223e96c254f383d0f906343eb67ad34d6c56711962fa8bfe097e75a2e41c696"),
            H("00cc786baa966e66f4a384c86a3b49942552e2d658a31ce2c344be4b91400da7d26d521628b00523b8dfe240c72de1f6"),
            H("1f86376e8981c217898751ad8746757d42aa7b90eeb791c09e4a3ec03251cf9de405aba9ec61deca6355c77b0e5f4cb"),
            H("08cc03fdefe0ff135caf4fe2a21529c4195536fbe3ce50b879833fd221351adc2ee7f8dc099040a841b6daecf2e8fedb"),
            H("16603fca40634b6a2211e11db8f0a6a074a7d0d4afadb7bd76505c3d3ad5544e203f6326c95a807299b23ab13633a5f0"),
            H("04ab0b9bcfac1bbcb2c977d027796b3ce75bb8ca2be184cb5231413c4d634f3747a87ac2460f415ec961f8855fe9d6f2"),
            H("0987c8d5333ab86fde9926bd2ca6c674170a05bfe3bdd81ffd038da6c26c842642f64550fedfe935a15e4ca31870fb29"),
            H("09fc4018bd96684be88c9e221e4da1bb8f3abd16679dc26c1e8b6e6a1f20cabe69d65201c78607a360370e577bdba587"),
            H("0e1bba7a1186bdb5223abde7ada14a23c42a0ca7915af6fe06985e7ed1e4d43b9b3f7055dd4eba6f2bafaaebca731c30"),
            H("19713e47937cd1be0dfd0b8f1d43fb93cd2fcbcb6caf493fd1183e416389e61031bf3a5cce3fbafce813711ad011c132"),
            H("18b46a908f36f6deb918c143fed2edcc523559b8aaf0c2462e6bfe7f911f643249d9cdf41b44d606ce07c8a4d0074d8e"),
            H("0b182cac101b9399d155096004f53f447aa7b12a3426b08ec02710e807b4633f06c851c1919211f20d4c04f00b971ef8"),
            H("0245a394ad1eca9b72fc00ae7be315dc757b3b080d4c158013e6632d3c40659cc6cf90ad1c232a6442d9d3f5db980133"),
            H("05c129645e44cf1102a159f748c4a3fc5e673d81d7e86568d9ab0f5d396a7ce46ba1049b6579afb7866b1e715475224b"),
            H("15e6be4e990f03ce4ea50b3b42df2eb5cb181d8f84965a3957add4fa95af01b2b665027efec01c7704b456be69c8b604"),
        ];

        BigInteger[] yDen = [
            H("16112c4c3a9c98b252181140fad0eae9601a6de578980be6eec3232b5be72e7a07f3688ef60c206d01479253b03663c1"),
            H("1962d75c2381201e1a0cbd6c43c348b885c84ff731c4d59ca4a10356f453e01f78a4260763529e3532f6102c2e49a03d"),
            H("058df3306640da276faaae7d6e8eb15778c4855551ae7f310c35a5dd279cd2eca6757cd636f96f891e2538b53dbf67f2"),
            H("16b7d288798e5395f20d23bf89edb4d1d115c5dbddbcd30e123da489e726af41727364f2c28297ada8d26d98445f5416"),
            H("0be0e079545f43e4b00cc912f8228ddcc6d19c9f0f69bbb0542eda0fc9dec916a20b15dc0fd2ededda39142311a5001d"),
            H("08d9e5297186db2d9fb266eaac783182b70152c65550d881c5ecd87b6f0f5a6449f38db9dfa9cce202c6477faaf9b7ac"),
            H("166007c08a99db2fc3ba8734ace9824b5eecfdfa8d0cf8ef5dd365bc400a0051d5fa9c01a58b1fb93d1a1399126a775c"),
            H("16a3ef08be3ea7ea03bcddfabba6ff6ee5a4375efa1f4fd7feb34fd206357132b920f5b00801dee460ee415a15812ed9"),
            H("1866c8ed336c61231a1be54fd1d74cc4f9fb0ce4c6af5920abc5750c4bf39b4852cfe2f7bb9248836b233d9d55535d4a"),
            H("167a55cda70a6e1cea820597d94a84903216f763e13d87bb5308592e7ea7d4fbc7385ea3d529b35e346ef48bb8913f55"),
            H("04d2f259eea405bd48f010a01ad2911d9c6dd039bb61a6290e591b36e636a5c871a5c29f4f83060400f8b49cba8f6aa8"),
            H("0accbb67481d033ff5852c1e48c50c477f94ff8aefce42d28c0f9a88cea7913516f968986f7ebbea9684b529e2561092"),
            H("0ad6b9514c767fe3c3613144b45f1496543346d98adf02267d5ceef9a00d9b8693000763e3b90ac11e99b138573345cc"),
            H("02660400eb2e4f3b628bdd0d53cd76f2bf565b94e72927c1cb748df27942480e420517bd8714cc80d1fadc1326ed06f7"),
            H("0e0fa1d816ddc03e6b24255e0d7819c171c40f65e273b853324efcd6356caa205ca2f570f13497804415473a1d634b8f"),
            BigInteger.One,
        ];

        return [xNum, xDen, yNum, yDen];
    }

    // 3-isogeny map coefficients for G2 (Fp2)
    private static Fp2Element[][] IsogenyCoeffsG2()
    {
        static BigInteger H(string hex)
        {
            return hex is "0x0" or "0" ? BigInteger.Zero :
            hex is "0x1" or "1" ? BigInteger.One :
            BigInteger.Parse(
                hex.StartsWith("0x", StringComparison.Ordinal) ? hex[2..] : hex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static Fp2Element F2(string c0, string c1)
        {
            return Fp2.Create(Fp.Create(H(c0)), Fp.Create(H(c1)));
        }

        Fp2Element[] xNum = [
            F2("5c759507e8e333ebb5b7a9a47d7ed8532c52d39fd3a042a88b58423c50ae15d5c2638e343d9c71c6238aaaaaaaa97d6",
               "5c759507e8e333ebb5b7a9a47d7ed8532c52d39fd3a042a88b58423c50ae15d5c2638e343d9c71c6238aaaaaaaa97d6"),
            F2("0",
               "11560bf17baa99bc32126fced787c88f984f87adf7ae0c7f9a208c6b4f20a4181472aaa9cb8d555526a9ffffffffc71a"),
            F2("11560bf17baa99bc32126fced787c88f984f87adf7ae0c7f9a208c6b4f20a4181472aaa9cb8d555526a9ffffffffc71e",
               "08ab05f8bdd54cde190937e76bc3e447cc27c3d6fbd7063fcd104635a790520c0a395554e5c6aaaa9354ffffffffe38d"),
            F2("171d6541fa38ccfaed6dea691f5fb614cb14b4e7f4e810aa22d6108f142b85757098e38d0f671c7188e2aaaaaaaa5ed1",
               "0"),
        ];

        Fp2Element[] xDen = [
            F2("0",
               "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaa63"),
            F2("0c",
               "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaa9f"),
            Fp2.ONE,
        ];

        Fp2Element[] yNum = [
            F2("1530477c7ab4113b59a4c18b076d11930f7da5d4a07f649bf54439d87d27e500fc8c25ebf8c92f6812cfc71c71c6d706",
               "1530477c7ab4113b59a4c18b076d11930f7da5d4a07f649bf54439d87d27e500fc8c25ebf8c92f6812cfc71c71c6d706"),
            F2("0",
               "5c759507e8e333ebb5b7a9a47d7ed8532c52d39fd3a042a88b58423c50ae15d5c2638e343d9c71c6238aaaaaaaa97be"),
            F2("11560bf17baa99bc32126fced787c88f984f87adf7ae0c7f9a208c6b4f20a4181472aaa9cb8d555526a9ffffffffc71c",
               "08ab05f8bdd54cde190937e76bc3e447cc27c3d6fbd7063fcd104635a790520c0a395554e5c6aaaa9354ffffffffe38f"),
            F2("124c9ad43b6cf79bfbf7043de3811ad0761b0f37a1e26286b0e977c69aa274524e79097a56dc4bd9e1b371c71c718b10",
               "0"),
        ];

        Fp2Element[] yDen = [
            F2("1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffa8fb",
               "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffa8fb"),
            F2("0",
               "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffa9d3"),
            F2("12",
               "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaa99"),
            Fp2.ONE,
        ];

        return [xNum, xDen, yNum, yDen];
    }

    #endregion
}
