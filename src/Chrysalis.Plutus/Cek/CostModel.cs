using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Saturating 64-bit arithmetic for cost model calculations.
/// Matches the Plutus Core specification behavior: clamps at long.MaxValue on positive overflow.
/// </summary>
internal static class SatMath
{
    internal static long Add(long a, long b)
    {
        long r = a + b;
        return (a >= 0 && b >= 0 && r < 0) ? long.MaxValue : r;
    }

    internal static long Mul(long a, long b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }

        Int128 r = (Int128)a * b;
        return r > long.MaxValue ? long.MaxValue : (long)r;
    }
}

/// <summary>
/// A cost function that evaluates to a number of CPU or MEM units given up to 3 argument sizes.
/// All builtin cost models are expressed as CostFunction pairs (one for CPU, one for MEM).
/// </summary>
internal abstract record CostFunction
{
    internal abstract long Eval(long x, long y, long z);

    protected static long TwoVarQuadratic(
        long minimum, long c00, long c10, long c01, long c20, long c11, long c02,
        long x, long y)
    {
        long raw = SatMath.Add(
            SatMath.Add(
                SatMath.Add(c00, SatMath.Mul(c10, x)),
                SatMath.Add(SatMath.Mul(c01, y), SatMath.Mul(c20, SatMath.Mul(x, x)))),
            SatMath.Add(
                SatMath.Mul(c11, SatMath.Mul(x, y)),
                SatMath.Mul(c02, SatMath.Mul(y, y))));
        return Math.Max(minimum, raw);
    }
}

internal sealed record ConstantCost(long Value) : CostFunction
{
    internal override long Eval(long x, long y, long z) => Value;
}

internal sealed record LinearCost(int ArgIndex, long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long v = ArgIndex switch { 0 => x, 1 => y, _ => z };
        return SatMath.Add(Intercept, SatMath.Mul(Slope, v));
    }

    internal static LinearCost InX(long intercept, long slope) => new(0, intercept, slope);

    internal static LinearCost InY(long intercept, long slope) => new(1, intercept, slope);

    internal static LinearCost InZ(long intercept, long slope) => new(2, intercept, slope);
}

internal sealed record AddedSizesCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, SatMath.Add(x, y)));
}

internal sealed record SubtractedSizesCost(long Intercept, long Slope, long Minimum) : CostFunction
{
    internal override long Eval(long x, long y, long z) => Math.Max(Minimum, SatMath.Add(Intercept, SatMath.Mul(Slope, x - y)));
}

internal sealed record MultipliedSizesCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, SatMath.Mul(x, y)));
}

internal sealed record MinSizeCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, Math.Min(x, y)));
}

internal sealed record MaxSizeCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, Math.Max(x, y)));
}

internal sealed record LinearOnDiagonalCost(long Intercept, long Slope, long ConstantValue) : CostFunction
{
    internal override long Eval(long x, long y, long z) => x == y ? SatMath.Add(Intercept, SatMath.Mul(Slope, x)) : ConstantValue;
}

internal sealed record QuadraticCost(int ArgIndex, long Coeff0, long Coeff1, long Coeff2) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long v = ArgIndex switch { 0 => x, 1 => y, _ => z };
        return SatMath.Add(
            SatMath.Add(Coeff0, SatMath.Mul(Coeff1, v)),
            SatMath.Mul(Coeff2, SatMath.Mul(v, v)));
    }

    internal static QuadraticCost InX(long c0, long c1, long c2) => new(0, c0, c1, c2);

    internal static QuadraticCost InY(long c0, long c1, long c2) => new(1, c0, c1, c2);

    internal static QuadraticCost InZ(long c0, long c1, long c2) => new(2, c0, c1, c2);
}

internal sealed record ConstAboveDiagonalCost(
    long ConstantValue, long Minimum,
    long C00, long C10, long C01, long C20, long C11, long C02
) : CostFunction
{
    internal override long Eval(long x, long y, long z) => x < y ? ConstantValue : TwoVarQuadratic(Minimum, C00, C10, C01, C20, C11, C02, x, y);
}

internal sealed record LiteralInYOrLinearInZCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => Math.Max(y, SatMath.Add(Intercept, SatMath.Mul(Slope, z)));
}

internal sealed record LinearInYAndZCost(long Intercept, long SlopeY, long SlopeZ) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Add(SatMath.Mul(SlopeY, y), SatMath.Mul(SlopeZ, z)));
}

internal sealed record LinearInMaxYZCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, Math.Max(y, z)));
}

internal sealed record ExpModCost(long Coeff00, long Coeff11, long Coeff12) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long yz = SatMath.Mul(y, z);
        long baseCost = SatMath.Add(
            Coeff00,
            SatMath.Add(SatMath.Mul(Coeff11, yz), SatMath.Mul(Coeff12, SatMath.Mul(yz, z))));
        return x > z ? SatMath.Add(baseCost, baseCost / 2) : baseCost;
    }
}

internal sealed record WithInteractionCost(long C00, long C10, long C01, long C11) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(
            SatMath.Add(C00, SatMath.Mul(C10, x)),
            SatMath.Add(SatMath.Mul(C01, y), SatMath.Mul(C11, SatMath.Mul(x, y))));
}

/// <summary>
/// Cost model for a single builtin function: one CostFunction for CPU, one for MEM.
/// When both are constant, CachedCost is precomputed to skip virtual dispatch + arg size computation.
/// </summary>
internal readonly struct BuiltinCostModel
{
    internal readonly CostFunction Cpu;
    internal readonly CostFunction Mem;
    internal readonly ExBudget CachedCost;
    internal readonly bool IsConstant;

    internal BuiltinCostModel(CostFunction cpu, CostFunction mem)
    {
        Cpu = cpu;
        Mem = mem;
        if (cpu is ConstantCost cc && mem is ConstantCost mc)
        {
            CachedCost = new ExBudget(cc.Value, mc.Value);
            IsConstant = true;
        }
        else
        {
            CachedCost = default;
            IsConstant = false;
        }
    }

    internal ExBudget Eval(long x, long y, long z) => new(Cpu.Eval(x, y, z), Mem.Eval(x, y, z));
}
