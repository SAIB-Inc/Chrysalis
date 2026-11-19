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
    /// <summary>The latest major protocol version supported by this evaluator.</summary>
    public const int LatestProtocolVersion = Availability.VanRossem;

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with CBOR-encoded PlutusData arguments.
    /// This is the core evaluation primitive.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes (inner script, not the CBOR-wrapped outer layer).</param>
    /// <param name="argumentsCbor">CBOR-encoded PlutusData arguments (e.g. datum, redeemer, script context).</param>
    /// <param name="plutusVersion">Plutus language version of the script.</param>
    /// <param name="protocolVersion">Major protocol version to evaluate at.</param>
    /// <param name="costModel">Flat cost-model array for the script's language. May be omitted only for Plutus V3 at the latest supported protocol version.</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// A Plutus V3 script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The Plutus language is not 1–3, or the protocol version is negative.</exception>
    /// <exception cref="NotSupportedException">The protocol version is newer than <see cref="LatestProtocolVersion"/>.</exception>
    /// <exception cref="ArgumentNullException">An argument is null, or the language and protocol version require a cost model.</exception>
    /// <exception cref="InvalidOperationException">The language or a decoded term is unavailable.</exception>
    /// <exception cref="EvaluationException">The Plutus Core version is unavailable or the script failed during evaluation.</exception>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<byte[]> argumentsCbor,
        int plutusVersion,
        int protocolVersion,
        IReadOnlyList<long>? costModel = null,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(argumentsCbor);

        (Term<DeBruijn> term, CostModel model) = Load(scriptBytes, plutusVersion, protocolVersion, costModel);

        // Apply each CBOR-encoded argument as (con data ...)
        foreach (byte[] argCbor in argumentsCbor)
        {
            PlutusData data = CborReader.DecodePlutusData(argCbor);
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(data)));
        }

        return Run(term, model, plutusVersion, budget, caseOnConstants: Availability.CaseOnConstants(protocolVersion));
    }

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with VM PlutusData arguments directly.
    /// Avoids CBOR round-trip when arguments are already in VM representation.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes.</param>
    /// <param name="arguments">VM PlutusData arguments (datum, redeemer, script context).</param>
    /// <param name="plutusVersion">Plutus language version of the script.</param>
    /// <param name="protocolVersion">Major protocol version to evaluate at.</param>
    /// <param name="costModel">Flat cost-model array for the script's language. May be omitted only for Plutus V3 at the latest supported protocol version.</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// A Plutus V3 script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The Plutus language is not 1–3, or the protocol version is negative.</exception>
    /// <exception cref="NotSupportedException">The protocol version is newer than <see cref="LatestProtocolVersion"/>.</exception>
    /// <exception cref="ArgumentNullException">An argument is null, or the language and protocol version require a cost model.</exception>
    /// <exception cref="InvalidOperationException">The language or a decoded term is unavailable.</exception>
    /// <exception cref="EvaluationException">The Plutus Core version is unavailable or the script failed during evaluation.</exception>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<PlutusData> arguments,
        int plutusVersion,
        int protocolVersion,
        IReadOnlyList<long>? costModel = null,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(arguments);

        (Term<DeBruijn> term, CostModel model) = Load(scriptBytes, plutusVersion, protocolVersion, costModel);

        foreach (PlutusData arg in arguments)
        {
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(arg)));
        }

        return Run(term, model, plutusVersion, budget, caseOnConstants: Availability.CaseOnConstants(protocolVersion));
    }

    internal static EvaluationResult Run(Term<DeBruijn> applied, CostModel model, int plutusVersion, ExBudget? budget, bool caseOnConstants = true)
    {
        CekMachine machine = new(budget, model, caseOnConstants);
        ValidateReturnValue(machine.RunValue(applied), plutusVersion);
        return new EvaluationResult(RedeemerTag: 0, Index: 0, ExUnits: UsedUnits(budget ?? ExBudget.Unlimited, machine.RemainingBudget));
    }

    internal static ExUnitsResult UsedUnits(ExBudget initial, ExBudget remaining) => new(
        Mem: (ulong)Math.Max(0, SatMath.Sub(initial.Mem, remaining.Mem)),
        Steps: (ulong)Math.Max(0, SatMath.Sub(initial.Cpu, remaining.Cpu)));

    private static (Term<DeBruijn>, CostModel) Load(byte[] scriptBytes, int plutusVersion, int protocolVersion, IReadOnlyList<long>? costModel)
    {
        Availability.Check(plutusVersion, protocolVersion);
        CostModel model = costModel is not null
            ? CostModelLoader.FromFlat(costModel, plutusVersion, Availability.VariantFor(plutusVersion, protocolVersion))
            : plutusVersion == 3 && protocolVersion == Availability.VanRossem
                ? CostModel.LatestV3
                : throw new ArgumentNullException(nameof(costModel), "A cost model is required except for Plutus V3 at protocol version 11.");
        Term<DeBruijn> term = FlatDecoder.DecodeProgramCore(scriptBytes, Availability.Builtins(plutusVersion, protocolVersion), out Types.Version version);
        if (!Availability.HasProgramVersion(plutusVersion, protocolVersion, version))
        {
            throw new EvaluationException($"Plutus Core {version} is not available for Plutus V{plutusVersion} at protocol version {protocolVersion}.");
        }
        return (term, model);
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
