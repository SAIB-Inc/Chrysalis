using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.Models;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>
/// Evaluator for Plutus scripts. Operates on raw bytes only — no Codec/Wallet dependencies.
/// Chrysalis.Tx is responsible for CBOR deserialization and stitching transaction context.
/// </summary>
public static class Evaluator
{
    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with CBOR-encoded PlutusData arguments.
    /// This is the core evaluation primitive.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes (inner script, not the CBOR-wrapped outer layer).</param>
    /// <param name="argumentsCbor">CBOR-encoded PlutusData arguments (e.g. datum, redeemer, script context).</param>
    /// <param name="costModel">Optional flat cost-model array of the script's language (from protocol parameters). Defaults to the latest baked-in model.</param>
    /// <param name="plutusVersion">Plutus language version of the script. Defaults to 3.</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// The script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="NotSupportedException">The script is Plutus V1 or V2.</exception>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<byte[]> argumentsCbor,
        IReadOnlyList<long>? costModel = null,
        int plutusVersion = 3,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(argumentsCbor);

        RequireSupportedVersion(plutusVersion);

        Program<DeBruijn> program = FlatDecoder.DecodeProgram(scriptBytes);
        Term<DeBruijn> term = program.Term;

        // Apply each CBOR-encoded argument as (con data ...)
        foreach (byte[] argCbor in argumentsCbor)
        {
            PlutusData data = CborReader.DecodePlutusData(argCbor);
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(data)));
        }

        return Run(term, costModel is null ? CostModel.LatestV3 : CostModelLoader.FromFlat(costModel, plutusVersion), plutusVersion, budget);
    }

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with VM PlutusData arguments directly.
    /// Avoids CBOR round-trip when arguments are already in VM representation.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes.</param>
    /// <param name="arguments">VM PlutusData arguments (datum, redeemer, script context).</param>
    /// <param name="costModel">Optional flat cost-model array of the script's language (from protocol parameters). Defaults to the latest baked-in model.</param>
    /// <param name="plutusVersion">Plutus language version of the script. Defaults to 3.</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// The script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="NotSupportedException">The script is Plutus V1 or V2.</exception>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<PlutusData> arguments,
        IReadOnlyList<long>? costModel = null,
        int plutusVersion = 3,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(arguments);

        RequireSupportedVersion(plutusVersion);

        Program<DeBruijn> program = FlatDecoder.DecodeProgram(scriptBytes);
        Term<DeBruijn> term = program.Term;

        foreach (PlutusData arg in arguments)
        {
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(arg)));
        }

        return Run(term, costModel is null ? CostModel.LatestV3 : CostModelLoader.FromFlat(costModel, plutusVersion), plutusVersion, budget);
    }

    internal static EvaluationResult Run(Term<DeBruijn> applied, CostModel model, int plutusVersion, ExBudget? budget)
    {
        CekMachine machine = new(budget, model);
        ValidateReturnValue(machine.RunValue(applied), plutusVersion);
        return new EvaluationResult(RedeemerTag: 0, Index: 0, ExUnits: UsedUnits(budget ?? ExBudget.Unlimited, machine.RemainingBudget));
    }

    internal static ExUnitsResult UsedUnits(ExBudget initial, ExBudget remaining) => new(
        Mem: (ulong)Math.Max(0, SatMath.Sub(initial.Mem, remaining.Mem)),
        Steps: (ulong)Math.Max(0, SatMath.Sub(initial.Cpu, remaining.Cpu)));

    private static void RequireSupportedVersion(int plutusVersion)
    {
        if (plutusVersion is 1 or 2)
        {
            throw new NotSupportedException("EvaluateScript does not yet support Plutus V1 or V2.");
        }
    }

    private static void ValidateReturnValue(CekValue result, int plutusVersion)
    {
        if (plutusVersion is not (1 or 2) && result is not VConstant { Value: UnitConstant })
        {
            throw new InvalidReturnValueException();
        }
    }

    /// <summary>
    /// Evaluates a raw UPLC term directly (already parsed/decoded).
    /// Lowest-level API — no Flat decoding or argument application.
    /// </summary>
    /// <param name="term">The UPLC term to evaluate.</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The resulting term and consumed execution units.</returns>
    public static (Term<DeBruijn> Result, ExUnitsResult ExUnits) EvaluateTerm(
        Term<DeBruijn> term,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(term);

        CekMachine machine = new(budget);
        Term<DeBruijn> result = machine.Run(term);

        return (result, UsedUnits(budget ?? ExBudget.Unlimited, machine.RemainingBudget));
    }
}
