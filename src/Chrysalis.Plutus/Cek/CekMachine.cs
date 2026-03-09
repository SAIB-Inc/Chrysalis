using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// CEK (Control-Environment-Kontinuation) abstract machine for evaluating UPLC programs.
/// Implements spec section 2.4, Figure 2.10.
/// </summary>
public sealed class CekMachine
{
    private ExBudget _budget;
    private readonly int[] _unbudgetedSteps;
    private readonly BuiltinCostModel[] _builtinCosts;

    // Machine state (mutable for zero-allocation hot loop)
    private Context _ctx = null!;
    private Environment? _env;
    private Term<DeBruijn>? _computeTerm;
    private CekValue? _returnValue;

    public CekMachine(ExBudget? initialBudget = null)
    {
        ExBudget initial = initialBudget ?? ExBudget.Unlimited;
        _budget = initial - MachineCosts.StartupCost;
        _unbudgetedSteps = new int[MachineCosts.StepKindCount + 1];
        _builtinCosts = DefaultCosts.Create();
    }

    internal CekMachine(ExBudget initialBudget, BuiltinCostModel[] builtinCosts)
    {
        _budget = initialBudget - MachineCosts.StartupCost;
        _unbudgetedSteps = new int[MachineCosts.StepKindCount + 1];
        _builtinCosts = builtinCosts;
    }

    public ExBudget RemainingBudget
    {
        get
        {
            SpendUnbudgetedSteps();
            return _budget;
        }
    }

    /// <summary>
    /// Evaluate a UPLC term and return the resulting term.
    /// </summary>
    public Term<DeBruijn> Run(Term<DeBruijn> term)
    {
        _ctx = NoFrame.Instance;
        _env = null;
        _computeTerm = term;
        _returnValue = null;

        while (true)
        {
            if (_computeTerm is not null)
            {
                ComputeStep();
            }
            else
            {
                if (_ctx is NoFrame)
                {
                    SpendUnbudgetedSteps();
                    return Discharge.DischargeValue(_returnValue!);
                }
                ReturnStep();
            }
        }
    }

    // --- Budget tracking ---

    private void StepAndMaybeSpend(int step)
    {
        _unbudgetedSteps[step]++;
        _unbudgetedSteps[MachineCosts.StepKindCount]++;
        if (_unbudgetedSteps[MachineCosts.StepKindCount] >= MachineCosts.Slippage)
        {
            SpendUnbudgetedSteps();
        }
    }

    private void SpendUnbudgetedSteps()
    {
        long cpu = 0;
        long mem = 0;
        for (int i = 0; i < MachineCosts.StepKindCount; i++)
        {
            int count = _unbudgetedSteps[i];
            if (count > 0)
            {
                cpu += count * MachineCosts.StepCost.Cpu;
                mem += count * MachineCosts.StepCost.Mem;
                _unbudgetedSteps[i] = 0;
            }
        }
        _budget = new ExBudget(_budget.Cpu - cpu, _budget.Mem - mem);
        _unbudgetedSteps[MachineCosts.StepKindCount] = 0;
    }

    // --- Compute step: evaluate a term in the current environment ---

    private void ComputeStep()
    {
        Term<DeBruijn> term = _computeTerm!;
        _computeTerm = null;

        switch (term)
        {
            case VarTerm<DeBruijn> v:
                StepAndMaybeSpend(MachineCosts.StepVar);
                _returnValue = Environment.Lookup(_env, v.Name.Index)
                    ?? throw new EvaluationException($"Unbound variable: index {v.Name.Index}");
                break;

            case ConstTerm<DeBruijn> c:
                StepAndMaybeSpend(MachineCosts.StepConstant);
                _returnValue = new VConstant(c.Value);
                break;

            case LambdaTerm<DeBruijn> l:
                StepAndMaybeSpend(MachineCosts.StepLambda);
                _returnValue = new VLambda(l.Parameter, l.Body, _env);
                break;

            case DelayTerm<DeBruijn> d:
                StepAndMaybeSpend(MachineCosts.StepDelay);
                _returnValue = new VDelay(d.Body, _env);
                break;

            case ForceTerm<DeBruijn> f:
                StepAndMaybeSpend(MachineCosts.StepForce);
                _ctx = new FrameForce(_ctx);
                _computeTerm = f.Body;
                break;

            case ApplyTerm<DeBruijn> a:
                StepAndMaybeSpend(MachineCosts.StepApply);
                _ctx = new FrameAwaitFunTerm(_env, a.Argument, _ctx);
                _computeTerm = a.Function;
                break;

            case BuiltinTerm<DeBruijn> b:
                StepAndMaybeSpend(MachineCosts.StepBuiltin);
                _returnValue = new VBuiltin(b.Function, 0, []);
                break;

            case ConstrTerm<DeBruijn> constr:
                StepAndMaybeSpend(MachineCosts.StepConstr);
                if (constr.Fields.IsEmpty)
                {
                    _returnValue = new VConstr(constr.Tag, []);
                }
                else
                {
                    _ctx = new FrameConstr(
                        _env, constr.Tag, constr.Fields, 1,
                        [], _ctx);
                    _computeTerm = constr.Fields[0];
                }
                break;

            case CaseTerm<DeBruijn> cs:
                StepAndMaybeSpend(MachineCosts.StepCase);
                _ctx = new FrameCases(_env, cs.Branches, _ctx);
                _computeTerm = cs.Scrutinee;
                break;

            case ErrorTerm<DeBruijn>:
                throw new EvaluationException("ExplicitErrorTerm");

            default:
                throw new EvaluationException($"Unknown term type: {term.GetType().Name}");
        }
    }

    // --- Return step: pop continuation frame and apply to current value ---

    private void ReturnStep()
    {
        CekValue value = _returnValue!;
        _returnValue = null;

        switch (_ctx)
        {
            case FrameAwaitFunTerm fat:
                _ctx = new FrameAwaitArg(value, fat.Ctx);
                _env = fat.Env;
                _computeTerm = fat.Term;
                break;

            case FrameAwaitArg faa:
                _ctx = faa.Ctx;
                ApplyEvaluate(faa.Value, value);
                break;

            case FrameAwaitFunValue fafv:
                _ctx = fafv.Ctx;
                ApplyEvaluate(value, fafv.Value);
                break;

            case FrameForce ff:
                _ctx = ff.Ctx;
                ForceEvaluate(value);
                break;

            case FrameConstr fc:
                {
                    ImmutableArray<CekValue> newResolved = fc.Resolved.Add(value);
                    if (fc.NextFieldIndex >= fc.Fields.Length)
                    {
                        _ctx = fc.Ctx;
                        _returnValue = new VConstr(fc.Index, newResolved);
                    }
                    else
                    {
                        _ctx = new FrameConstr(
                            fc.Env, fc.Index, fc.Fields,
                            fc.NextFieldIndex + 1, newResolved, fc.Ctx);
                        _env = fc.Env;
                        _computeTerm = fc.Fields[fc.NextFieldIndex];
                    }
                    break;
                }

            case FrameCases fcs:
                {
                    ulong tag;
                    ImmutableArray<CekValue> fields;
                    if (value is VConstr constr)
                    {
                        tag = constr.Index;
                        fields = constr.Fields;
                    }
                    else if (value is VConstant vc)
                    {
                        (tag, fields) = ConstantToConstr(vc.Value, fcs.Branches.Length);
                    }
                    else
                    {
                        throw new EvaluationException(
                            $"case: expected constr or constant, got {value.GetType().Name}");
                    }

                    if (tag >= (ulong)fcs.Branches.Length)
                    {
                        throw new EvaluationException(
                            $"case: constructor tag {tag} out of range ({fcs.Branches.Length} branches)");
                    }

                    _ctx = ContextHelpers.TransferArgStack(fields, fcs.Ctx);
                    _env = fcs.Env;
                    _computeTerm = fcs.Branches[(int)tag];
                    break;
                }

            default:
                throw new EvaluationException($"Unknown context frame: {_ctx.GetType().Name}");
        }
    }

    // --- Apply: function application ---

    private void ApplyEvaluate(CekValue fun, CekValue arg)
    {
        switch (fun)
        {
            case VLambda lambda:
                _env = Environment.Extend(lambda.Env, arg);
                _computeTerm = lambda.Body;
                break;

            case VBuiltin builtin:
                {
                    int expectedForces = builtin.Function.ForceCount();
                    int expectedArity = builtin.Function.Arity();
                    if (builtin.Forces < expectedForces)
                    {
                        throw new EvaluationException(
                            $"builtin {builtin.Function}: expected {expectedForces} force(s), got {builtin.Forces}");
                    }

                    ImmutableArray<CekValue> newArgs = builtin.Args.Add(arg);
                    _returnValue = newArgs.Length == expectedArity
                        ? CallBuiltin(builtin.Function, newArgs)
                        : new VBuiltin(builtin.Function, builtin.Forces, newArgs);
                    break;
                }

            default:
                throw new EvaluationException(
                    $"NonFunctionalApplication: cannot apply {fun.GetType().Name}");
        }
    }

    // --- Force: polymorphic instantiation ---

    private void ForceEvaluate(CekValue value)
    {
        switch (value)
        {
            case VDelay delay:
                _env = delay.Env;
                _computeTerm = delay.Body;
                break;

            case VBuiltin builtin:
                {
                    int expectedForces = builtin.Function.ForceCount();
                    if (builtin.Forces >= expectedForces)
                    {
                        throw new EvaluationException(
                            $"builtin {builtin.Function}: too many forces (expected {expectedForces})");
                    }

                    int newForces = builtin.Forces + 1;
                    int expectedArity = builtin.Function.Arity();
                    _returnValue = newForces == expectedForces && expectedArity == 0
                        ? CallBuiltin(builtin.Function, [])
                        : new VBuiltin(builtin.Function, newForces, builtin.Args);
                    break;
                }

            default:
                throw new EvaluationException(
                    $"NonPolymorphicInstantiation: cannot force {value.GetType().Name}");
        }
    }

    // --- Builtin invocation with cost tracking ---

    private CekValue CallBuiltin(DefaultFunction func, ImmutableArray<CekValue> args)
    {
        (long x, long y, long z) = ExMem.ComputeArgSizes(func, args);
        BuiltinCostModel model = _builtinCosts[(int)func];
        ExBudget cost = model.Eval(x, y, z);
        _budget -= cost;
        return Builtins.BuiltinRuntime.Call(func, args);
    }

    // --- Constant case decomposition (UPLC 1.1.0) ---

    private static (ulong Tag, ImmutableArray<CekValue> Fields) ConstantToConstr(
        Constant c, int numBranches)
    {
        switch (c)
        {
            case IntegerConstant i:
                {
                    BigInteger n = i.Value;
                    return n < 0
                        ? throw new EvaluationException("case: negative integer tag")
                        : n > ulong.MaxValue
                            ? throw new EvaluationException("case: integer tag too large")
                            : ((ulong)n, []);
                }

            case BoolConstant b:
                if (numBranches > 2)
                {
                    throw new EvaluationException(
                        $"case: bool expects at most 2 branches, got {numBranches}");
                }

                return (b.Value ? 1UL : 0UL, []);

            case UnitConstant:
                if (numBranches > 1)
                {
                    throw new EvaluationException(
                        $"case: unit expects at most 1 branch, got {numBranches}");
                }

                return (0UL, []);

            case PairConstant p:
                if (numBranches > 1)
                {
                    throw new EvaluationException(
                        $"case: pair expects at most 1 branch, got {numBranches}");
                }

                return (0UL, [
                    new VConstant(p.First),
                    new VConstant(p.Second)]);

            case ListConstant list:
                if (numBranches > 2)
                {
                    throw new EvaluationException(
                        $"case: list expects at most 2 branches, got {numBranches}");
                }

                if (list.Values.IsEmpty)
                {
                    return (1UL, []);
                }

                return (0UL, [
                    new VConstant(list.Values[0]),
                    new VConstant(new ListConstant(list.ItemType, list.Values.RemoveAt(0)))]);

            default:
                throw new EvaluationException(
                    $"case: cannot case on constant of type {c.GetType().Name}");
        }
    }
}
