using System.Collections.Immutable;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>Startup and CEK step costs, in ascending StepKind order.</summary>
internal sealed class MachineCosts
{
    internal ExBudget Startup { get; }

    internal ImmutableArray<ExBudget> Steps { get; }

    internal ExBudget? UniformStep { get; }

    internal static MachineCosts Default => CostModel.Default.Machine;

    internal MachineCosts(ExBudget startup, ImmutableArray<ExBudget> steps)
    {
        Startup = startup;
        Steps = steps;
        ExBudget first = steps[0];
        if (first.Cpu is >= 0 and <= long.MaxValue / Slippage && first.Mem is >= 0 and <= long.MaxValue / Slippage)
        {
            foreach (ExBudget cost in steps)
            {
                if (cost != first)
                {
                    return;
                }
            }
            UniformStep = first;
        }
    }

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
