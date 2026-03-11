using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// CEK (Control-Environment-Kontinuation) abstract machine for evaluating UPLC programs.
/// Implements spec section 2.4, Figure 2.10.
/// </summary>
public sealed class CekMachine
{
    private ExBudget _budget;
    private int _unbudgetedSteps;
    private readonly BuiltinCostModel[] _builtinCosts;

    // Machine state (mutable for zero-allocation hot loop)
    private readonly ContextStack _ctxStack = new();
    private Environment? _env;
    private Term<DeBruijn>? _computeTerm;
    private CekValue? _returnValue;

    public CekMachine(ExBudget? initialBudget = null)
    {
        ExBudget initial = initialBudget ?? ExBudget.Unlimited;
        _budget = initial - MachineCosts.StartupCost;
        _unbudgetedSteps = 0;
        _builtinCosts = DefaultCosts.Create();
    }

    internal CekMachine(ExBudget initialBudget, BuiltinCostModel[] builtinCosts)
    {
        _budget = initialBudget - MachineCosts.StartupCost;
        _unbudgetedSteps = 0;
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
        _ctxStack.Reset();
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
                if (_ctxStack.IsEmpty)
                {
                    SpendUnbudgetedSteps();
                    return Discharge.DischargeValue(_returnValue!);
                }
                ReturnStep();
            }
        }
    }

    // --- Budget tracking ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StepAndMaybeSpend()
    {
        if (++_unbudgetedSteps >= MachineCosts.Slippage)
        {
            SpendUnbudgetedSteps();
        }
    }

    private void SpendUnbudgetedSteps()
    {
        int count = _unbudgetedSteps;
        _budget = new ExBudget(
            _budget.Cpu - (count * MachineCosts.StepCost.Cpu),
            _budget.Mem - (count * MachineCosts.StepCost.Mem));
        _unbudgetedSteps = 0;
    }

    // --- Compute step: evaluate a term in the current environment ---

    private void ComputeStep()
    {
        Term<DeBruijn> term = _computeTerm!;
        _computeTerm = null;

        switch (term.TermTag)
        {
            case TermTag.Var:
                {
                    VarTerm<DeBruijn> v = Unsafe.As<VarTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    _returnValue = Environment.Lookup(_env, v.Name.Index)
                        ?? throw new EvaluationException($"Unbound variable: index {v.Name.Index}");
                    break;
                }

            case TermTag.Const:
                {
                    ConstTerm<DeBruijn> c = Unsafe.As<ConstTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    _returnValue = new VConstant(c.Value);
                    break;
                }

            case TermTag.Lambda:
                {
                    LambdaTerm<DeBruijn> l = Unsafe.As<LambdaTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    _returnValue = new VLambda(l.Parameter, l.Body, _env);
                    break;
                }

            case TermTag.Delay:
                {
                    DelayTerm<DeBruijn> d = Unsafe.As<DelayTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    _returnValue = new VDelay(d.Body, _env);
                    break;
                }

            case TermTag.Force:
                {
                    ForceTerm<DeBruijn> f = Unsafe.As<ForceTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    // Fast path: Force(Delay(body)) — skip VDelay allocation + frame roundtrip
                    if (f.Body.TermTag == TermTag.Delay)
                    {
                        DelayTerm<DeBruijn> d = Unsafe.As<DelayTerm<DeBruijn>>(f.Body);
                        StepAndMaybeSpend(); // charge for the Delay step
                        _computeTerm = d.Body;
                    }
                    // Fast path: Force(Builtin(f)) — skip unforced VBuiltin + frame roundtrip
                    else if (f.Body.TermTag == TermTag.Builtin)
                    {
                        BuiltinTerm<DeBruijn> b = Unsafe.As<BuiltinTerm<DeBruijn>>(f.Body);
                        StepAndMaybeSpend(); // charge for the Builtin step
                        DefaultFunction func = b.Function;
                        int expectedForces = func.ForceCount();
                        if (expectedForces < 1)
                        {
                            throw new EvaluationException(
                                $"builtin {func}: too many forces (expected {expectedForces})");
                        }
                        int expectedArity = func.Arity();
                        _returnValue = 1 == expectedForces && expectedArity == 0
                            ? CallBuiltin(func, [])
                            : new VBuiltin(func, 1,
                                expectedArity > 0 ? new CekValue[expectedArity] : [], 0);
                    }
                    else
                    {
                        ref ContextFrame frame = ref _ctxStack.Push();
                        frame.Tag = FrameTag.Force;
                        _computeTerm = f.Body;
                    }
                    break;
                }

            case TermTag.Apply:
                {
                    ApplyTerm<DeBruijn> a = Unsafe.As<ApplyTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    // Fast path: Apply(Lambda(body), arg) — skip VLambda allocation + 2 frame steps
                    if (a.Function.TermTag == TermTag.Lambda)
                    {
                        LambdaTerm<DeBruijn> l = Unsafe.As<LambdaTerm<DeBruijn>>(a.Function);
                        StepAndMaybeSpend(); // charge for the Lambda step
                        ref ContextFrame frame = ref _ctxStack.Push();
                        frame.Tag = FrameTag.AwaitArgDirect;
                        frame.Env = _env;
                        frame.Term = l.Body;
                        _computeTerm = a.Argument;
                    }
                    else
                    {
                        ref ContextFrame frame = ref _ctxStack.Push();
                        frame.Tag = FrameTag.AwaitFunTerm;
                        frame.Env = _env;
                        frame.Term = a.Argument;
                        _computeTerm = a.Function;
                    }
                    break;
                }

            case TermTag.Builtin:
                {
                    BuiltinTerm<DeBruijn> b = Unsafe.As<BuiltinTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    _returnValue = new VBuiltin(b.Function, 0, [], 0);
                    break;
                }

            case TermTag.Constr:
                {
                    ConstrTerm<DeBruijn> constr = Unsafe.As<ConstrTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    if (constr.Fields.IsEmpty)
                    {
                        _returnValue = new VConstr(constr.Tag, [], 0);
                    }
                    else
                    {
                        ref ContextFrame frame = ref _ctxStack.Push();
                        frame.Tag = FrameTag.Constr;
                        frame.Env = _env;
                        frame.ConstrIndex = constr.Tag;
                        frame.ConstrFields = constr.Fields;
                        frame.NextFieldIndex = 1;
                        frame.Resolved = new CekValue[constr.Fields.Length];
                        frame.ResolvedCount = 0;
                        _computeTerm = constr.Fields[0];
                    }
                    break;
                }

            case TermTag.Case:
                {
                    CaseTerm<DeBruijn> cs = Unsafe.As<CaseTerm<DeBruijn>>(term);
                    StepAndMaybeSpend();
                    ref ContextFrame frame = ref _ctxStack.Push();
                    frame.Tag = FrameTag.Cases;
                    frame.Env = _env;
                    frame.Branches = cs.Branches;
                    _computeTerm = cs.Scrutinee;
                    break;
                }

            case TermTag.Error:
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

        switch (_ctxStack.PeekTag())
        {
            case FrameTag.AwaitFunTerm:
                {
                    // Transform in-place: AwaitFunTerm → AwaitArg
                    ref ContextFrame f = ref _ctxStack.Peek();
                    Environment? env = f.Env;
                    Term<DeBruijn>? term = f.Term;
                    f.Tag = FrameTag.AwaitArg;
                    f.Value = value;
                    f.Term = null;
                    f.Env = null;
                    _env = env;
                    _computeTerm = term;
                    break;
                }

            case FrameTag.AwaitArg:
                {
                    ref ContextFrame f = ref _ctxStack.Pop();
                    CekValue funValue = f.Value!;
                    f.Value = null;
                    ApplyEvaluate(funValue, value);
                    break;
                }

            case FrameTag.AwaitArgDirect:
                {
                    // Direct lambda application: extend env and evaluate body
                    ref ContextFrame f = ref _ctxStack.Pop();
                    _env = Environment.Extend(f.Env, value);
                    _computeTerm = f.Term;
                    f.Term = null;
                    f.Env = null;
                    break;
                }

            case FrameTag.AwaitFunValue:
                {
                    ref ContextFrame f = ref _ctxStack.Pop();
                    CekValue argValue = f.Value!;
                    f.Value = null;
                    ApplyEvaluate(value, argValue);
                    break;
                }

            case FrameTag.Force:
                _ctxStack.Drop();
                ForceEvaluate(value);
                break;

            case FrameTag.Constr:
                {
                    ref ContextFrame fc = ref _ctxStack.Peek();
                    fc.Resolved![fc.ResolvedCount++] = value;

                    if (fc.NextFieldIndex >= fc.ConstrFields.Length)
                    {
                        CekValue[] resolved = fc.Resolved;
                        int resolvedCount = fc.ResolvedCount;
                        ulong constrIndex = fc.ConstrIndex;
                        _ctxStack.Drop();
                        _returnValue = new VConstr(constrIndex, resolved, resolvedCount);
                    }
                    else
                    {
                        _env = fc.Env;
                        _computeTerm = fc.ConstrFields[fc.NextFieldIndex];
                        fc.NextFieldIndex++;
                    }
                    break;
                }

            case FrameTag.Cases:
                {
                    // Extract all needed data BEFORE any push (Pop ref is invalidated by Push)
                    ref ContextFrame fcs = ref _ctxStack.Pop();
                    ImmutableArray<Term<DeBruijn>> branches = fcs.Branches;
                    Environment? casesEnv = fcs.Env;
                    int branchCount = branches.Length;
                    fcs.Env = null;

                    ulong tag;
                    CekValue[]? fields;
                    int fieldCount;

                    if (value is VConstr constr)
                    {
                        tag = constr.Index;
                        fields = constr.Fields;
                        fieldCount = constr.FieldCount;
                    }
                    else if (value is VConstant vc)
                    {
                        (tag, fields, fieldCount) = ConstantToConstr(vc.Value, branchCount);
                    }
                    else
                    {
                        throw new EvaluationException(
                            $"case: expected constr or constant, got {value.GetType().Name}");
                    }

                    if (tag >= (ulong)branchCount)
                    {
                        throw new EvaluationException(
                            $"case: constructor tag {tag} out of range ({branchCount} branches)");
                    }

                    if (fields != null && fieldCount > 0)
                    {
                        _ctxStack.TransferArgStack(fields, fieldCount);
                    }
                    _env = casesEnv;
                    _computeTerm = branches[(int)tag];
                    break;
                }

            case FrameTag.NoFrame:
            default:
                throw new EvaluationException("Unexpected NoFrame in return step");
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

                    int newArgCount = builtin.ArgCount + 1;

                    // Reuse existing array if it has capacity, otherwise allocate
                    CekValue[] newArgs;
                    if (builtin.Args.Length >= newArgCount)
                    {
                        newArgs = builtin.Args;
                    }
                    else
                    {
                        newArgs = new CekValue[expectedArity];
                        if (builtin.ArgCount > 0)
                        {
                            Array.Copy(builtin.Args, newArgs, builtin.ArgCount);
                        }
                    }
                    newArgs[builtin.ArgCount] = arg;

                    _returnValue = newArgCount == expectedArity
                        ? CallBuiltin(builtin.Function, newArgs)
                        : new VBuiltin(builtin.Function, builtin.Forces, newArgs, newArgCount);
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
                        : new VBuiltin(builtin.Function, newForces, builtin.Args, builtin.ArgCount);
                    break;
                }

            default:
                throw new EvaluationException(
                    $"NonPolymorphicInstantiation: cannot force {value.GetType().Name}");
        }
    }

    // --- Builtin invocation with cost tracking ---

    private CekValue CallBuiltin(DefaultFunction func, CekValue[] args)
    {
        BuiltinCostModel model = _builtinCosts[(int)func];
        if (model.IsConstant)
        {
            _budget -= model.CachedCost;
        }
        else
        {
            (long x, long y, long z) = ExMem.ComputeArgSizes(func, args);
            _budget -= model.Eval(x, y, z);
        }
        return Builtins.BuiltinRuntime.Call(func, args);
    }

    // --- Constant case decomposition (UPLC 1.1.0) ---

    private static (ulong Tag, CekValue[]? Fields, int FieldCount) ConstantToConstr(
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
                            : ((ulong)n, null, 0);
                }

            case BoolConstant b:
                if (numBranches > 2)
                {
                    throw new EvaluationException(
                        $"case: bool expects at most 2 branches, got {numBranches}");
                }

                return (b.Value ? 1UL : 0UL, null, 0);

            case UnitConstant:
                if (numBranches > 1)
                {
                    throw new EvaluationException(
                        $"case: unit expects at most 1 branch, got {numBranches}");
                }

                return (0UL, null, 0);

            case PairConstant p:
                if (numBranches > 1)
                {
                    throw new EvaluationException(
                        $"case: pair expects at most 1 branch, got {numBranches}");
                }

                return (0UL, [
                    new VConstant(p.First),
                    new VConstant(p.Second)], 2);

            case ListConstant list:
                if (numBranches > 2)
                {
                    throw new EvaluationException(
                        $"case: list expects at most 2 branches, got {numBranches}");
                }

                if (list.IsListEmpty)
                {
                    return (1UL, null, 0);
                }

                return (0UL, [
                    new VConstant(list.ElementAt(0)),
                    new VConstant(new ListConstant(list.ItemType, list.Values) { Offset = list.Offset + 1 })], 2);

            default:
                throw new EvaluationException(
                    $"case: cannot case on constant of type {c.GetType().Name}");
        }
    }
}
