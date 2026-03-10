namespace Chrysalis.Plutus.Types;

/// <summary>
/// Execution budget tracking CPU steps and memory units.
/// Both are signed 64-bit to match the Haskell Int64 representation
/// and to detect overspend (negative values).
/// </summary>
public readonly record struct ExBudget(long Cpu, long Mem)
{
    public static readonly ExBudget Zero = new(0, 0);
    public static readonly ExBudget Unlimited = new(long.MaxValue, long.MaxValue);

    public static ExBudget operator +(ExBudget a, ExBudget b)
    {
        return new(SatAdd(a.Cpu, b.Cpu), SatAdd(a.Mem, b.Mem));
    }

    public static ExBudget operator -(ExBudget a, ExBudget b)
    {
        return new(a.Cpu - b.Cpu, a.Mem - b.Mem);
    }

    public bool IsExhausted => Cpu < 0 || Mem < 0;

    private static long SatAdd(long a, long b)
    {
        long r = a + b;
        return r < a ? long.MaxValue : r;
    }
}
