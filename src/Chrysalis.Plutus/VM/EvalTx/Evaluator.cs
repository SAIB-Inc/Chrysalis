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
    /// <summary>The protocol version of the current networks and the default of <c>ScriptContextBuilder.EvaluateTx</c>: 11.
    /// <c>EvaluateScript</c> also accepts 12 (Dijkstra) with the rules of plutus 1.71.0.0 (node 11.2.1), experimental until the Dijkstra hard-fork node.
    /// Protocol version 12 and Plutus V4 are experimental and follow the node's pinned Plutus; batch 7 builtins are present but not yet enabled by any network.</summary>
    public const int LatestProtocolVersion = Availability.VanRossem;

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with CBOR-encoded PlutusData arguments.
    /// This is the core evaluation primitive.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes (inner script, not the CBOR-wrapped outer layer).</param>
    /// <param name="argumentsCbor">CBOR-encoded PlutusData arguments (e.g. datum, redeemer, script context).</param>
    /// <param name="plutusVersion">Plutus language version of the script: 1 to 4. Plutus V4 is experimental and only at protocol version 12; it follows the node's pinned Plutus.</param>
    /// <param name="protocolVersion">Major protocol version to evaluate at.</param>
    /// <param name="costModel">Flat cost-model array for the script's language. May be omitted only for Plutus V3 at protocol version 11 (<see cref="LatestProtocolVersion"/>).</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// A Plutus V3 or V4 script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The Plutus language is not 1–4, or the protocol version is negative.</exception>
    /// <exception cref="NotSupportedException">The protocol version is above 12.</exception>
    /// <exception cref="ArgumentNullException">An argument is null, or the language and protocol version require a cost model.</exception>
    /// <exception cref="InvalidOperationException">The language or a decoded term is unavailable.</exception>
    /// <exception cref="UnboundVariableException">
    /// The script has a variable no enclosing lambda binds, outside <c>constr</c> and <c>case</c>; the node refuses it before running it.
    /// </exception>
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

        (Term<DeBruijn> term, CostModel model, Caser caser) = Load(scriptBytes, plutusVersion, protocolVersion, costModel);

        // Apply each CBOR-encoded argument as (con data ...)
        foreach (byte[] argCbor in argumentsCbor)
        {
            PlutusData data = CborReader.DecodePlutusData(argCbor);
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(data)));
        }

        return Run(term, model, plutusVersion, budget, caser);
    }

    /// <summary>
    /// Evaluates a single Flat-encoded Plutus script with VM PlutusData arguments directly.
    /// Avoids CBOR round-trip when arguments are already in VM representation.
    /// </summary>
    /// <param name="scriptBytes">Flat-encoded script bytes.</param>
    /// <param name="arguments">VM PlutusData arguments (datum, redeemer, script context).</param>
    /// <param name="plutusVersion">Plutus language version of the script: 1 to 4. Plutus V4 is experimental and only at protocol version 12; it follows the node's pinned Plutus.</param>
    /// <param name="protocolVersion">Major protocol version to evaluate at.</param>
    /// <param name="costModel">Flat cost-model array for the script's language. May be omitted only for Plutus V3 at protocol version 11 (<see cref="LatestProtocolVersion"/>).</param>
    /// <param name="budget">Enforced: the script fails with BudgetExceededException when either part falls below zero. Leave it out to measure.</param>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    /// <returns>The evaluation result with consumed execution units.</returns>
    /// <exception cref="InvalidReturnValueException">
    /// A Plutus V3 or V4 script returned a value other than built-in unit.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The Plutus language is not 1–4, or the protocol version is negative.</exception>
    /// <exception cref="NotSupportedException">The protocol version is above 12.</exception>
    /// <exception cref="ArgumentNullException">An argument is null, or the language and protocol version require a cost model.</exception>
    /// <exception cref="InvalidOperationException">The language or a decoded term is unavailable.</exception>
    /// <exception cref="UnboundVariableException">
    /// The script has a variable no enclosing lambda binds, outside <c>constr</c> and <c>case</c>; the node refuses it before running it.
    /// </exception>
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

        (Term<DeBruijn> term, CostModel model, Caser caser) = Load(scriptBytes, plutusVersion, protocolVersion, costModel);

        foreach (PlutusData arg in arguments)
        {
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(new DataConstant(arg)));
        }

        return Run(term, model, plutusVersion, budget, caser);
    }

    internal static EvaluationResult Run(Term<DeBruijn> applied, CostModel model, int plutusVersion, ExBudget? budget, Caser caser = Caser.Full)
    {
        CekMachine machine = new(budget, model, caser);
        ValidateReturnValue(machine.RunValue(applied), plutusVersion);
        return new EvaluationResult(RedeemerTag: 0, Index: 0, ExUnits: UsedUnits(budget ?? ExBudget.Unlimited, machine.RemainingBudget));
    }

    internal static ExUnitsResult UsedUnits(ExBudget initial, ExBudget remaining) => new(
        Mem: (ulong)Math.Max(0, SatMath.Sub(initial.Mem, remaining.Mem)),
        Steps: (ulong)Math.Max(0, SatMath.Sub(initial.Cpu, remaining.Cpu)));

    private static (Term<DeBruijn>, CostModel, Caser) Load(byte[] scriptBytes, int plutusVersion, int protocolVersion, IReadOnlyList<long>? costModel)
    {
        Availability.Check(plutusVersion, protocolVersion);
        CostModel model = costModel is not null
            ? CostModelLoader.FromFlat(costModel, plutusVersion, Availability.VariantFor(plutusVersion, protocolVersion))
            : plutusVersion == 3 && protocolVersion == Availability.VanRossem
                ? CostModel.LatestV3
                : throw new ArgumentNullException(nameof(costModel), "A cost model is required except for Plutus V3 at protocol version 11.");
        Term<DeBruijn> term = FlatDecoder.DecodeProgramCore(scriptBytes, Availability.Builtins(plutusVersion, protocolVersion), Availability.DecodeLimits(protocolVersion), out Types.Version version);
        if (!Availability.HasProgramVersion(plutusVersion, protocolVersion, version))
        {
            throw new EvaluationException($"Plutus Core {version} is not available for Plutus V{plutusVersion} at protocol version {protocolVersion}.");
        }

        if (!IsClosed(term, 0))
        {
            throw new UnboundVariableException();
        }
        return (term, model, Availability.CaserFor(protocolVersion, version));
    }

    // Upstream's scope check (Check/Scope.hs:33-55; Common/Eval.hs:130): a variable must be bound by an
    // enclosing lambda, index 1 to the depth. Constr and case are not looked into, so a variable there
    // fails only if it is evaluated. Flat decodes every binder as 0 (FlatDecoder.Close).
    internal static bool IsClosed(Term<DeBruijn> term, int depth)
    {
        PooledStack<(Term<DeBruijn> Term, int Depth)> pending = default;
        bool rented = false;
        try
        {
            while (true)
            {
                switch (term)
                {
                    case VarTerm<DeBruijn> variable:
                        if (variable.Name.Index < 1 || variable.Name.Index > depth)
                        {
                            return false;
                        }

                        break;
                    case LambdaTerm<DeBruijn> lambda:
                        term = lambda.Body;
                        depth++;
                        continue;
                    case DelayTerm<DeBruijn> delay:
                        term = delay.Body;
                        continue;
                    case ForceTerm<DeBruijn> force:
                        term = force.Body;
                        continue;
                    case ApplyTerm<DeBruijn> apply:
                        if (!rented)
                        {
                            pending = new(16);
                            rented = true;
                        }

                        pending.Push((apply.Argument, depth));
                        term = apply.Function;
                        continue;
                    default:
                        break;
                }

                if (pending.Count == 0)
                {
                    return true;
                }

                (term, depth) = pending.Pop();
            }
        }
        finally
        {
            pending.Dispose();
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
    /// Does not check scope; an unbound variable fails with <see cref="EvaluationException"/> when evaluated.
    /// Case on <c>Data.Constr</c> is available, as on plutus's default machine.
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
