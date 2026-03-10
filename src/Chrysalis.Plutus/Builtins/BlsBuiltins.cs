using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Crypto.Bls12381;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// BLS12-381 builtin implementations using Chrysalis.Crypto.
/// </summary>
internal static class BlsBuiltins
{
    // BLS12-381 field order (Fr)
    private static readonly BigInteger FrOrder = BigInteger.Parse(
        "073eda753299d7d483339d80809a1d80553bda402fffe5bfeffffffff00000001",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    // Scalar bounds for multiScalarMul: |scalar| <= 2^4095
    private static readonly BigInteger MsmScalarUb = (BigInteger.One << 4095) - 1;
    private static readonly BigInteger MsmScalarLb = -(BigInteger.One << 4095);

    // --- G1 operations ---

    internal static CekValue Bls12_381_G1_Add(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapG1(args[0]).ToArray();
        byte[] b = UnwrapG1(args[1]).ToArray();
        return WrapBlsCall(() => G1Result(Bls12381.G1Add(a, b)));
    }

    internal static CekValue Bls12_381_G1_Neg(ImmutableArray<CekValue> args)
    {
        byte[] p = UnwrapG1(args[0]).ToArray();
        return WrapBlsCall(() => G1Result(Bls12381.G1Neg(p)));
    }

    internal static CekValue Bls12_381_G1_ScalarMul(ImmutableArray<CekValue> args)
    {
        BigInteger scalar = UnwrapInteger(args[0]);
        byte[] p = UnwrapG1(args[1]).ToArray();
        return WrapBlsCall(() => G1Result(Bls12381.G1ScalarMul(scalar, p)));
    }

    internal static CekValue Bls12_381_G1_Equal(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapG1(args[0]).ToArray();
        byte[] b = UnwrapG1(args[1]).ToArray();
        return WrapBlsCall(() => BoolResult(Bls12381.G1Equal(a, b)));
    }

    internal static CekValue Bls12_381_G1_Compress(ImmutableArray<CekValue> args)
    {
        return ByteStringResult(UnwrapG1(args[0]));
    }

    internal static CekValue Bls12_381_G1_Uncompress(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[0]);
        if (bs.Length != 48)
        {
            throw new EvaluationException(
                $"bls12_381_G1_uncompress: expected 48 bytes, got {bs.Length}");
        }

        byte[] compressed = bs.ToArray();
        return WrapBlsCall(() =>
        {
            PointG1 point = PointG1.Uncompress(compressed);
            return G1Result(point.Compress());
        });
    }

    internal static CekValue Bls12_381_G1_HashToGroup(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> msg = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> dst = UnwrapByteString(args[1]);
        return dst.Length > 255
            ? throw new EvaluationException("bls12_381_G1_hashToGroup: DST must be at most 255 bytes")
            : WrapBlsCall(() => G1Result(Bls12381.G1HashToGroup(msg.ToArray(), dst.ToArray())));
    }

    internal static CekValue Bls12_381_G1_MultiScalarMul(ImmutableArray<CekValue> args)
    {
        ImmutableArray<Constant> scalars = UnwrapList(args[0]);
        ImmutableArray<Constant> points = UnwrapList(args[1]);
        int n = Math.Min(scalars.Length, points.Length);
        return n == 0
            ? G1Result(G1ZeroCompressed)
            : WrapBlsCall(() => MultiScalarMulG1(scalars, points, n));
    }

    // --- G2 operations ---

    internal static CekValue Bls12_381_G2_Add(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapG2(args[0]).ToArray();
        byte[] b = UnwrapG2(args[1]).ToArray();
        return WrapBlsCall(() => G2Result(Bls12381.G2Add(a, b)));
    }

    internal static CekValue Bls12_381_G2_Neg(ImmutableArray<CekValue> args)
    {
        byte[] p = UnwrapG2(args[0]).ToArray();
        return WrapBlsCall(() => G2Result(Bls12381.G2Neg(p)));
    }

    internal static CekValue Bls12_381_G2_ScalarMul(ImmutableArray<CekValue> args)
    {
        BigInteger scalar = UnwrapInteger(args[0]);
        byte[] p = UnwrapG2(args[1]).ToArray();
        return WrapBlsCall(() => G2Result(Bls12381.G2ScalarMul(scalar, p)));
    }

    internal static CekValue Bls12_381_G2_Equal(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapG2(args[0]).ToArray();
        byte[] b = UnwrapG2(args[1]).ToArray();
        return WrapBlsCall(() => BoolResult(Bls12381.G2Equal(a, b)));
    }

    internal static CekValue Bls12_381_G2_Compress(ImmutableArray<CekValue> args)
    {
        return ByteStringResult(UnwrapG2(args[0]));
    }

    internal static CekValue Bls12_381_G2_Uncompress(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[0]);
        if (bs.Length != 96)
        {
            throw new EvaluationException(
                $"bls12_381_G2_uncompress: expected 96 bytes, got {bs.Length}");
        }

        byte[] compressed = bs.ToArray();
        return WrapBlsCall(() =>
        {
            PointG2 point = PointG2.Uncompress(compressed);
            return G2Result(point.Compress());
        });
    }

    internal static CekValue Bls12_381_G2_HashToGroup(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> msg = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> dst = UnwrapByteString(args[1]);
        return dst.Length > 255
            ? throw new EvaluationException("bls12_381_G2_hashToGroup: DST must be at most 255 bytes")
            : WrapBlsCall(() => G2Result(Bls12381.G2HashToGroup(msg.ToArray(), dst.ToArray())));
    }

    internal static CekValue Bls12_381_G2_MultiScalarMul(ImmutableArray<CekValue> args)
    {
        ImmutableArray<Constant> scalars = UnwrapList(args[0]);
        ImmutableArray<Constant> points = UnwrapList(args[1]);
        int n = Math.Min(scalars.Length, points.Length);
        return n == 0
            ? G2Result(G2ZeroCompressed)
            : WrapBlsCall(() => MultiScalarMulG2(scalars, points, n));
    }

    // --- Pairing operations ---

    internal static CekValue Bls12_381_MillerLoop(ImmutableArray<CekValue> args)
    {
        byte[] g1 = UnwrapG1(args[0]).ToArray();
        byte[] g2 = UnwrapG2(args[1]).ToArray();
        return WrapBlsCall(() => MlResult(Bls12381.MillerLoop(g1, g2)));
    }

    internal static CekValue Bls12_381_MulMlResult(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapMlResult(args[0]).ToArray();
        byte[] b = UnwrapMlResult(args[1]).ToArray();
        return WrapBlsCall(() => MlResult(Bls12381.MulMlResult(a, b)));
    }

    internal static CekValue Bls12_381_FinalVerify(ImmutableArray<CekValue> args)
    {
        byte[] a = UnwrapMlResult(args[0]).ToArray();
        byte[] b = UnwrapMlResult(args[1]).ToArray();
        return WrapBlsCall(() => BoolResult(Bls12381.FinalVerify(a, b)));
    }

    private static CekValue WrapBlsCall(Func<CekValue> call)
    {
        try
        {
            return call();
        }
        catch (EvaluationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EvaluationException($"BLS12-381 error: {ex.Message}", ex);
        }
    }

    // --- Private helpers ---

    // Compressed representations of identity points
    private static readonly byte[] G1ZeroCompressed = CreateG1Zero();
    private static readonly byte[] G2ZeroCompressed = CreateG2Zero();

    private static byte[] CreateG1Zero()
    {
        byte[] z = new byte[48];
        z[0] = 0xC0;
        return z;
    }

    private static byte[] CreateG2Zero()
    {
        byte[] z = new byte[96];
        z[0] = 0xC0;
        return z;
    }

    private static void ValidateMsmScalar(BigInteger s)
    {
        if (s > MsmScalarUb || s < MsmScalarLb)
        {
            throw new EvaluationException("multiScalarMul: scalar out of bounds");
        }
    }

    private static CekValue MultiScalarMulG1(ImmutableArray<Constant> scalars, ImmutableArray<Constant> points, int n)
    {
        BigInteger s0 = ((IntegerConstant)scalars[0]).Value;
        ValidateMsmScalar(s0);
        byte[] result = Bls12381.G1ScalarMul(
            ReduceScalar(s0),
            ((Bls12381G1Constant)points[0]).Value.ToArray());

        for (int i = 1; i < n; i++)
        {
            BigInteger s = ((IntegerConstant)scalars[i]).Value;
            ValidateMsmScalar(s);
            byte[] sp = Bls12381.G1ScalarMul(
                ReduceScalar(s),
                ((Bls12381G1Constant)points[i]).Value.ToArray());
            result = Bls12381.G1Add(result, sp);
        }

        return G1Result(result);
    }

    private static CekValue MultiScalarMulG2(ImmutableArray<Constant> scalars, ImmutableArray<Constant> points, int n)
    {
        BigInteger s0 = ((IntegerConstant)scalars[0]).Value;
        ValidateMsmScalar(s0);
        byte[] result = Bls12381.G2ScalarMul(
            ReduceScalar(s0),
            ((Bls12381G2Constant)points[0]).Value.ToArray());

        for (int i = 1; i < n; i++)
        {
            BigInteger s = ((IntegerConstant)scalars[i]).Value;
            ValidateMsmScalar(s);
            byte[] sp = Bls12381.G2ScalarMul(
                ReduceScalar(s),
                ((Bls12381G2Constant)points[i]).Value.ToArray());
            result = Bls12381.G2Add(result, sp);
        }

        return G2Result(result);
    }

    internal static BigInteger ReduceScalar(BigInteger scalar)
    {
        BigInteger s = scalar % FrOrder;
        if (s < 0)
        {
            s += FrOrder;
        }

        return s;
    }
}
