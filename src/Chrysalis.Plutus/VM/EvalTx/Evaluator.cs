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
    /// <param name="budget">Optional execution budget. Defaults to unlimited.</param>
    /// <returns>The evaluation result with consumed execution units.</returns>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<byte[]> argumentsCbor,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(argumentsCbor);

        Program<DeBruijn> program = FlatDecoder.DecodeProgram(scriptBytes);
        Term<DeBruijn> term = program.Term;

        // Apply each CBOR-encoded argument as (con data ...)
        foreach (byte[] argCbor in argumentsCbor)
        {
            PlutusData data = CborReader.DecodePlutusData(argCbor);
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(data)));
        }

        ExBudget initial = budget ?? ExBudget.Unlimited;
        CekMachine machine = new(initial);
        _ = machine.Run(term);

        ExBudget remaining = machine.RemainingBudget;
        long cpuUsed = initial.Cpu - remaining.Cpu;
        long memUsed = initial.Mem - remaining.Mem;

        return new EvaluationResult(
            RedeemerTag: 0,
            Index: 0,
            ExUnits: new ExUnitsResult(
                Mem: (ulong)Math.Max(0, memUsed),
                Steps: (ulong)Math.Max(0, cpuUsed)));
    }

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with VM PlutusData arguments directly.
    /// Avoids CBOR round-trip when arguments are already in VM representation.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes.</param>
    /// <param name="arguments">VM PlutusData arguments (datum, redeemer, script context).</param>
    /// <param name="budget">Optional execution budget. Defaults to unlimited.</param>
    /// <returns>The evaluation result with consumed execution units.</returns>
    public static EvaluationResult EvaluateScript(
        byte[] scriptBytes,
        IReadOnlyList<PlutusData> arguments,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(arguments);

        Program<DeBruijn> program = FlatDecoder.DecodeProgram(scriptBytes);
        Term<DeBruijn> term = program.Term;

        foreach (PlutusData arg in arguments)
        {
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(arg)));
        }

        ExBudget initial = budget ?? ExBudget.Unlimited;
        CekMachine machine = new(initial);
        _ = machine.Run(term);

        ExBudget remaining = machine.RemainingBudget;
        long cpuUsed = initial.Cpu - remaining.Cpu;
        long memUsed = initial.Mem - remaining.Mem;

        return new EvaluationResult(
            RedeemerTag: 0,
            Index: 0,
            ExUnits: new ExUnitsResult(
                Mem: (ulong)Math.Max(0, memUsed),
                Steps: (ulong)Math.Max(0, cpuUsed)));
    }

    /// <summary>
    /// Evaluates a raw UPLC term directly (already parsed/decoded).
    /// Lowest-level API — no Flat decoding or argument application.
    /// </summary>
    /// <param name="term">The UPLC term to evaluate.</param>
    /// <param name="budget">Optional execution budget. Defaults to unlimited.</param>
    /// <returns>The resulting term and consumed execution units.</returns>
    public static (Term<DeBruijn> Result, ExUnitsResult ExUnits) EvaluateTerm(
        Term<DeBruijn> term,
        ExBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(term);

        ExBudget initial = budget ?? ExBudget.Unlimited;
        CekMachine machine = new(initial);
        Term<DeBruijn> result = machine.Run(term);

        ExBudget remaining = machine.RemainingBudget;
        long cpuUsed = initial.Cpu - remaining.Cpu;
        long memUsed = initial.Mem - remaining.Mem;

        return (result, new ExUnitsResult(
            Mem: (ulong)Math.Max(0, memUsed),
            Steps: (ulong)Math.Max(0, cpuUsed)));
    }
}
