using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// Shared unwrap/result helpers for builtin implementations.
/// </summary>
internal static class BuiltinHelpers
{
    // --- Unwrap helpers ---

    internal static BigInteger UnwrapInteger(CekValue val) =>
        val is VConstant { Value: IntegerConstant i }
            ? i.Value
            : throw new EvaluationException(
                $"expected integer constant, got {DescribeValue(val)}");

    internal static ReadOnlyMemory<byte> UnwrapByteString(CekValue val) =>
        val is VConstant { Value: ByteStringConstant bs }
            ? bs.Value
            : throw new EvaluationException(
                $"expected bytestring constant, got {DescribeValue(val)}");

    internal static string UnwrapString(CekValue val) =>
        val is VConstant { Value: StringConstant s }
            ? s.Value
            : throw new EvaluationException(
                $"expected string constant, got {DescribeValue(val)}");

    internal static bool UnwrapBool(CekValue val) =>
        val is VConstant { Value: BoolConstant b }
            ? b.Value
            : throw new EvaluationException(
                $"expected bool constant, got {DescribeValue(val)}");

    internal static PlutusData UnwrapData(CekValue val) =>
        val is VConstant { Value: DataConstant d }
            ? d.Value
            : throw new EvaluationException(
                $"expected data constant, got {DescribeValue(val)}");

    internal static ImmutableArray<Constant> UnwrapList(CekValue val) =>
        val is VConstant { Value: ListConstant l }
            ? l.Values
            : throw new EvaluationException(
                $"expected list constant, got {DescribeValue(val)}");

    internal static ListConstant UnwrapListConstant(CekValue val) =>
        val is VConstant { Value: ListConstant l }
            ? l
            : throw new EvaluationException(
                $"expected list constant, got {DescribeValue(val)}");

    internal static Constant UnwrapConstant(CekValue val) =>
        val is VConstant c
            ? c.Value
            : throw new EvaluationException(
                $"expected constant, got {val.GetType().Name}");

    internal static ArrayConstant UnwrapArrayConstant(CekValue val) =>
        val is VConstant { Value: ArrayConstant a }
            ? a
            : throw new EvaluationException(
                $"expected array constant, got {DescribeValue(val)}");

    internal static ReadOnlyMemory<byte> UnwrapG1(CekValue val) =>
        val is VConstant { Value: Bls12381G1Constant g }
            ? g.Value
            : throw new EvaluationException(
                $"expected bls12_381_g1_element, got {DescribeValue(val)}");

    internal static ReadOnlyMemory<byte> UnwrapG2(CekValue val) =>
        val is VConstant { Value: Bls12381G2Constant g }
            ? g.Value
            : throw new EvaluationException(
                $"expected bls12_381_g2_element, got {DescribeValue(val)}");

    internal static ReadOnlyMemory<byte> UnwrapMlResult(CekValue val) =>
        val is VConstant { Value: Bls12381MlResultConstant m }
            ? m.Value
            : throw new EvaluationException(
                $"expected bls12_381_ml_result, got {DescribeValue(val)}");

    internal static void UnwrapUnit(CekValue val)
    {
        if (val is not VConstant { Value: UnitConstant })
        {
            throw new EvaluationException(
                $"expected unit constant, got {DescribeValue(val)}");
        }
    }

    // --- Result builders ---

    internal static CekValue IntegerResult(BigInteger n) =>
        new VConstant(new IntegerConstant(n));

    internal static CekValue ByteStringResult(byte[] bs) =>
        new VConstant(new ByteStringConstant(bs));

    internal static CekValue ByteStringResult(ReadOnlyMemory<byte> bs) =>
        new VConstant(new ByteStringConstant(bs));

    internal static CekValue BoolResult(bool b) =>
        new VConstant(new BoolConstant(b));

    internal static CekValue StringResult(string s) =>
        new VConstant(new StringConstant(s));

    internal static CekValue DataResult(PlutusData d) =>
        new VConstant(new DataConstant(d));

    internal static CekValue UnitResult() =>
        new VConstant(UnitConstant.Instance);

    internal static CekValue G1Result(byte[] bytes) =>
        new VConstant(new Bls12381G1Constant(bytes));

    internal static CekValue G2Result(byte[] bytes) =>
        new VConstant(new Bls12381G2Constant(bytes));

    internal static CekValue MlResult(byte[] bytes) =>
        new VConstant(new Bls12381MlResultConstant(bytes));

    internal static LedgerValue UnwrapValue(CekValue val) =>
        val is VConstant { Value: ValueConstant v }
            ? v.Value
            : throw new EvaluationException(
                $"expected value constant, got {DescribeValue(val)}");

    internal static CekValue ValueResult(LedgerValue v) =>
        new VConstant(new ValueConstant(v));

    // --- Description ---

    private static string DescribeValue(CekValue val) =>
        val is VConstant c ? c.Value.GetType().Name : val.GetType().Name;
}
