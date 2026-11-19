using Chrysalis.Plutus.Cek;

namespace Chrysalis.Plutus.Types;

/// <summary>
/// Execution budget tracking CPU steps and memory units.
/// Both are signed 64-bit to match the Haskell Int64 representation
/// and to detect overspend (negative values).
/// </summary>
public readonly record struct ExBudget(long Cpu, long Mem)
{
    public static readonly ExBudget Zero = new(0, 0);
    /// <summary>The largest budget, enforced like any other; a charge that makes either part negative stops the run.</summary>
    public static readonly ExBudget Unlimited = new(long.MaxValue, long.MaxValue);

    public static ExBudget operator +(ExBudget a, ExBudget b) => new(SatMath.Add(a.Cpu, b.Cpu), SatMath.Add(a.Mem, b.Mem));

    public static ExBudget operator -(ExBudget a, ExBudget b) => new(SatMath.Sub(a.Cpu, b.Cpu), SatMath.Sub(a.Mem, b.Mem));

    public bool IsExhausted => Cpu < 0 || Mem < 0;
}
