using System.Collections.Immutable;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>Saturating arithmetic matching Plutus SatInt.</summary>
internal static class SatMath
{
    internal static long Add(long a, long b)
    {
        long sum = unchecked(a + b);
        return ((a ^ sum) & (b ^ sum)) < 0 ? (a < 0 ? long.MinValue : long.MaxValue) : sum;
    }

    internal static long Sub(long a, long b)
    {
        long difference = unchecked(a - b);
        return ((a ^ b) & (a ^ difference)) < 0 ? (a < 0 ? long.MinValue : long.MaxValue) : difference;
    }

    // Upstream dividedBy retains saturated bounds and otherwise rounds toward -infinity.
    internal static long DivFloor(long value, long divisor)
    {
        if (value is long.MinValue or long.MaxValue)
        {
            return value;
        }
        long quotient = value / divisor;
        long remainder = value % divisor;
        return remainder != 0 && (remainder ^ divisor) < 0 ? quotient - 1 : quotient;
    }

    internal static long Mul(long a, long b)
    {
        Int128 product = (Int128)a * b;
        return product > long.MaxValue ? long.MaxValue : product < long.MinValue ? long.MinValue : (long)product;
    }
}

/// <summary>
/// A cost function that evaluates to a number of CPU or MEM units given up to 3 argument sizes.
/// All builtin cost models are expressed as CostFunction pairs (one for CPU, one for MEM).
/// </summary>
internal abstract record CostFunction
{
    internal abstract long Eval(long x, long y, long z);
}

internal sealed record ConstantCost(long Constant) : CostFunction
{
    internal override long Eval(long x, long y, long z) => Constant;
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
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, Math.Max(Minimum, SatMath.Sub(x, y))));
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

internal sealed record LinearOnDiagonalCost(long Intercept, long Slope, long Constant) : CostFunction
{
    internal override long Eval(long x, long y, long z) => x == y ? SatMath.Add(Intercept, SatMath.Mul(Slope, x)) : Constant;
}

internal sealed record QuadraticCost(int ArgIndex, long C0, long C1, long C2) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long v = ArgIndex switch { 0 => x, 1 => y, _ => z };
        return SatMath.Add(C0, SatMath.Add(SatMath.Mul(SatMath.Mul(C2, v), v), SatMath.Mul(C1, v)));
    }

    internal static QuadraticCost InX(long c0, long c1, long c2) => new(0, c0, c1, c2);

    internal static QuadraticCost InY(long c0, long c1, long c2) => new(1, c0, c1, c2);

    internal static QuadraticCost InZ(long c0, long c1, long c2) => new(2, c0, c1, c2);
}

internal sealed record ConstAboveDiagonalCost(long Constant, CostFunction Model) : CostFunction
{
    internal override long Eval(long x, long y, long z) => x < y ? Constant : Model.Eval(x, y, z);
}

internal sealed record AboveAndBelowDiagonalCost(CostFunction Model) : CostFunction
{
    internal override long Eval(long x, long y, long z) => Model.Eval(Math.Max(x, y), Math.Min(x, y), z);
}

internal sealed record QuadraticInXAndYCost(long Minimum, long C00, long C10, long C01, long C20, long C11, long C02) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long cost = SatMath.Add(C00, SatMath.Mul(C10, x));
        cost = SatMath.Add(cost, SatMath.Mul(C01, y));
        cost = SatMath.Add(cost, SatMath.Mul(SatMath.Mul(C20, x), x));
        cost = SatMath.Add(cost, SatMath.Mul(SatMath.Mul(C11, x), y));
        return Math.Max(Minimum, SatMath.Add(cost, SatMath.Mul(SatMath.Mul(C02, y), y)));
    }
}

internal sealed record LinearInXAndYCost(long Intercept, long Slope1, long Slope2) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Add(SatMath.Mul(Slope1, x), SatMath.Mul(Slope2, y)));
}

internal sealed record LiteralInYOrLinearInZCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => y != 0 ? y : SatMath.Add(Intercept, SatMath.Mul(Slope, z));
}

internal sealed record LinearInYAndZCost(long Intercept, long Slope1, long Slope2) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Add(SatMath.Mul(Slope1, y), SatMath.Mul(Slope2, z)));
}

internal sealed record LinearInMaxYZCost(long Intercept, long Slope) : CostFunction
{
    internal override long Eval(long x, long y, long z) => SatMath.Add(Intercept, SatMath.Mul(Slope, Math.Max(y, z)));
}

internal sealed record ExpModCost(long Coefficient00, long Coefficient11, long Coefficient12) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long cost = SatMath.Add(Coefficient00, SatMath.Mul(SatMath.Mul(Coefficient11, y), z));
        cost = SatMath.Add(cost, SatMath.Mul(SatMath.Mul(SatMath.Mul(Coefficient12, y), z), z));
        return x > z ? SatMath.Add(cost, SatMath.DivFloor(cost, 2)) : cost;
    }
}

internal sealed record WithInteractionCost(long C00, long C10, long C01, long C11) : CostFunction
{
    internal override long Eval(long x, long y, long z)
    {
        long cost = SatMath.Add(SatMath.Mul(C10, x), SatMath.Mul(C01, y));
        return SatMath.Add(SatMath.Add(cost, SatMath.Mul(C11, SatMath.Mul(x, y))), C00);
    }
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
            CachedCost = new ExBudget(cc.Constant, mc.Constant);
            IsConstant = true;
        }
        else
        {
            CachedCost = default;
            IsConstant = false;
        }
    }

    // Spend one total per builtin. For multi-element sizes this matches chunked spending
    // for nonnegative coefficients; PV11's only negative coefficients are single-value quadratics.
    internal ExBudget Eval(long x, long y, long z) => new(Cpu.Eval(x, y, z), Mem.Eval(x, y, z));
}

internal sealed record CostModel(ImmutableArray<BuiltinCostModel> Builtins, MachineCosts Machine, Variant Variant)
{
    internal static CostModel Default { get; } = CostModelLoader.Build(DefaultCosts.VariantC, ParamNames.V3, Variant.C);

    internal static CostModel LatestV3 { get; } = CostModelLoader.FromFlat(DefaultCosts.LatestV3, 3);
}
