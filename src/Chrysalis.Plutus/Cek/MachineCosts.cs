using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Per-step machine costs and startup cost.
/// All step kinds cost 16000 CPU + 100 MEM (matching Cardano mainnet protocol parameters).
/// </summary>
internal static class MachineCosts
{
    internal static readonly ExBudget StepCost = new(16000, 100);
    internal static readonly ExBudget StartupCost = new(100, 100);

    internal const int StepConstant = 0;
    internal const int StepVar = 1;
    internal const int StepLambda = 2;
    internal const int StepApply = 3;
    internal const int StepDelay = 4;
    internal const int StepForce = 5;
    internal const int StepBuiltin = 6;
    internal const int StepConstr = 7;
    internal const int StepCase = 8;
    internal const int StepKindCount = 9;

    internal const int Slippage = 200;
}
