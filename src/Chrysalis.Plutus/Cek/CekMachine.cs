using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

internal enum BudgetStop : byte
{
    None,
    Startup,
    Steps,
    Builtin
}

/// <summary>
/// CEK (Control-Environment-Kontinuation) abstract machine for evaluating UPLC programs.
/// Implements spec section 2.4, Figure 2.10.
/// </summary>
public sealed class CekMachine
{
    private ulong _pendingKinds;
    private int _unbudgetedSteps;
    private readonly ImmutableArray<BuiltinCostModel> _builtinCosts;
    private readonly MachineCosts _machineCosts;
    private readonly bool _measuring;
    private readonly Variant _variant;
    private readonly bool _caseOnConstants;

    // Machine state (mutable for zero-allocation hot loop)
    private readonly ContextStack _ctxStack = new();
    private Environment? _env;
    private Term<DeBruijn>? _computeTerm;
    private CekValue? _returnValue;

    private DebugState? _debug;

    private sealed class DebugState(int builtinCount)
    {
        internal readonly int[] Steps = new int[MachineCosts.StepKindCount];
        internal readonly long[] BuiltinCpu = new long[builtinCount];
        internal readonly long[] BuiltinMem = new long[builtinCount];
    }

    public CekMachine(ExBudget? initialBudget = null)
        : this(initialBudget, CostModel.Default, caseOnConstants: true)
    {
    }

    internal CekMachine(ExBudget initialBudget, BuiltinCostModel[] builtinCosts)
        : this(initialBudget, ImmutableCollectionsMarshal.AsImmutableArray(builtinCosts), MachineCosts.Default, Variant.C, true)
    {
    }

    internal CekMachine(ExBudget? initialBudget, CostModel model, bool caseOnConstants = true)
        : this(initialBudget, model.Builtins, model.Machine, model.Variant, caseOnConstants)
    {
    }

    private CekMachine(ExBudget? initialBudget, ImmutableArray<BuiltinCostModel> builtinCosts, MachineCosts machineCosts, Variant variant, bool caseOnConstants)
    {
        RemainingBudget = initialBudget ?? ExBudget.Unlimited;
        _measuring = initialBudget is null;
        _builtinCosts = builtinCosts;
        _machineCosts = machineCosts;
        _variant = variant;
        _caseOnConstants = caseOnConstants;
    }

    /// <summary>Enables per-step-kind and per-builtin cost tracking for debugging.</summary>
    public CekMachine EnableDebugCounters()
    {
        _debug = new(_builtinCosts.Length);
        return this;
    }

    /// <summary>Returns (stepCounts[9], builtinCpu[N], builtinMem[N]) if debug enabled.</summary>
    public (int[]? Steps, long[]? BuiltinCpu, long[]? BuiltinMem) DebugCounters => (_debug?.Steps, _debug?.BuiltinCpu, _debug?.BuiltinMem);

    /// <summary>The budget left, negative in the part that ran out after a budget stop.</summary>
    public ExBudget RemainingBudget { get; private set; }

    /// <summary>
    /// Evaluate a UPLC term and return the resulting term.
    /// </summary>
    /// <exception cref="BudgetExceededException">A charge took CPU or memory below zero.</exception>
    public Term<DeBruijn> Run(Term<DeBruijn> term) => TryRun(term, out Term<DeBruijn>? result) ? result : throw new BudgetExceededException();

    internal BudgetStop StoppedAt { get; private set; }

    internal bool TryRun(Term<DeBruijn> term, [NotNullWhen(true)] out Term<DeBruijn>? result)
    {
        if (TryRunValue(term, out CekValue? value))
        {
            result = Discharge.DischargeValue(value);
            return true;
        }
        result = null;
        return false;
    }

    internal CekValue RunValue(Term<DeBruijn> term) => TryRunValue(term, out CekValue? result) ? result : throw new BudgetExceededException();

    internal bool TryRunValue(Term<DeBruijn> term, [NotNullWhen(true)] out CekValue? result)
    {
        _ctxStack.Reset();
        _env = null;
        _computeTerm = term;
        _returnValue = null;
        _pendingKinds = 0;
        _unbudgetedSteps = 0;
        StoppedAt = BudgetStop.None;
        result = null;

        RemainingBudget -= _machineCosts.Startup;
        if (!_measuring && RemainingBudget.IsExhausted)
        {
            return Halt(BudgetStop.Startup);
        }

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
                    if (StoppedAt != BudgetStop.None || !SpendUnbudgetedSteps())
                    {
                        return false;
                    }
                    result = _returnValue!;
                    return true;
                }
                ReturnStep();
            }
        }
    }

    // --- Budget tracking ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool StepAndMaybeSpend(int stepKind)
    {
        if (_debug is not null)
        {
            _debug.Steps[stepKind]++;
        }
        if (stepKind != MachineCosts.StepCase)
        {
            // Eight packed bytes cannot carry: the total flushes at 200 steps.
            _pendingKinds += 1UL << (stepKind * 8);
        }
        return ++_unbudgetedSteps < MachineCosts.Slippage || SpendUnbudgetedSteps();
    }

    private bool SpendUnbudgetedSteps()
    {
        if (_unbudgetedSteps == 0)
        {
            return true;
        }
        int remaining = _unbudgetedSteps;
        ulong kinds = _pendingKinds;
        for (int kind = 0; kind < MachineCosts.StepKindCount; kind++)
        {
            // The ninth kind (Case) is the total minus the other eight.
            int count = kind == MachineCosts.StepCase ? remaining : (byte)kinds;
            kinds >>= 8;
            remaining -= count;
            if (count != 0)
            {
                ExBudget step = _machineCosts.Steps[kind];
                RemainingBudget -= new ExBudget(SatMath.Mul(count, step.Cpu), SatMath.Mul(count, step.Mem));
                if (!_measuring && RemainingBudget.IsExhausted)
                {
                    return Halt(BudgetStop.Steps);
                }
            }
        }
        _pendingKinds = 0;
        _unbudgetedSteps = 0;
        return true;
    }

    private bool Halt(BudgetStop site)
    {
        StoppedAt = site;
        _pendingKinds = 0;
        _unbudgetedSteps = 0;
        _ctxStack.Reset();
        _computeTerm = null;
        _returnValue = null;
        _env = null;
        return false;
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
                    if (!StepAndMaybeSpend(MachineCosts.StepVar))
                    {
                        break;
                    }
                    _returnValue = Environment.Lookup(_env, v.Name.Index)
                        ?? throw new EvaluationException($"Unbound variable: index {v.Name.Index}");
                    break;
                }

            case TermTag.Const:
                {
                    ConstTerm<DeBruijn> c = Unsafe.As<ConstTerm<DeBruijn>>(term);
                    if (!StepAndMaybeSpend(MachineCosts.StepConstant))
                    {
                        break;
                    }
                    _returnValue = new VConstant(c.Value);
                    break;
                }

            case TermTag.Lambda:
                {
                    LambdaTerm<DeBruijn> l = Unsafe.As<LambdaTerm<DeBruijn>>(term);
                    if (!StepAndMaybeSpend(MachineCosts.StepLambda))
                    {
                        break;
                    }
                    _returnValue = new VLambda(l.Parameter, l.Body, _env);
                    break;
                }

            case TermTag.Delay:
                {
                    DelayTerm<DeBruijn> d = Unsafe.As<DelayTerm<DeBruijn>>(term);
                    if (!StepAndMaybeSpend(MachineCosts.StepDelay))
                    {
                        break;
                    }
                    _returnValue = new VDelay(d.Body, _env);
                    break;
                }

            case TermTag.Force:
                {
                    ForceTerm<DeBruijn> f = Unsafe.As<ForceTerm<DeBruijn>>(term);
                    if (!StepAndMaybeSpend(MachineCosts.StepForce))
                    {
                        break;
                    }
                    // Fast path: Force(Delay(body)) — skip VDelay allocation + frame roundtrip
                    if (f.Body.TermTag == TermTag.Delay)
                    {
                        DelayTerm<DeBruijn> d = Unsafe.As<DelayTerm<DeBruijn>>(f.Body);
                        if (!StepAndMaybeSpend(MachineCosts.StepDelay))
                        {
                            break;
                        }
                        _computeTerm = d.Body;
                    }
                    // Fast path: Force(Builtin(f)) — skip unforced VBuiltin + frame roundtrip
                    else if (f.Body.TermTag == TermTag.Builtin)
                    {
                        BuiltinTerm<DeBruijn> b = Unsafe.As<BuiltinTerm<DeBruijn>>(f.Body);
                        if (!StepAndMaybeSpend(MachineCosts.StepBuiltin))
                        {
                            break;
                        }
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
                    if (!StepAndMaybeSpend(MachineCosts.StepApply))
                    {
                        break;
                    }
                    // Fast path: Apply(Lambda(body), arg) — skip VLambda allocation + 2 frame steps
                    if (a.Function.TermTag == TermTag.Lambda)
                    {
                        LambdaTerm<DeBruijn> l = Unsafe.As<LambdaTerm<DeBruijn>>(a.Function);
                        if (!StepAndMaybeSpend(MachineCosts.StepLambda))
                        {
                            break;
                        }
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
                    if (!StepAndMaybeSpend(MachineCosts.StepBuiltin))
                    {
                        break;
                    }
                    _returnValue = new VBuiltin(b.Function, 0, [], 0);
                    break;
                }

            case TermTag.Constr:
                {
                    ConstrTerm<DeBruijn> constr = Unsafe.As<ConstrTerm<DeBruijn>>(term);
                    if (!StepAndMaybeSpend(MachineCosts.StepConstr))
                    {
                        break;
                    }
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
                    if (!StepAndMaybeSpend(MachineCosts.StepCase))
                    {
                        break;
                    }
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
                    fcs.Branches = default;

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
                        if (!_caseOnConstants)
                        {
                            throw new EvaluationException("case on a builtin constant is not available before protocol version 11");
                        }
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

                    // Always allocate a new array — never reuse builtin.Args.
                    // The same VBuiltin can be looked up from the environment multiple
                    // times (e.g., a shared `force (builtin mkCons)` variable).
                    // Reusing the array causes aliasing: nested applications overwrite
                    // earlier arguments, producing wrong results.
                    CekValue[] newArgs = new CekValue[expectedArity];
                    if (builtin.ArgCount > 0)
                    {
                        Array.Copy(builtin.Args, newArgs, builtin.ArgCount);
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

    private CekValue? CallBuiltin(DefaultFunction func, CekValue[] args)
    {
        BuiltinCostModel model = _builtinCosts[(int)func];
        ExBudget cost;
        if (model.IsConstant)
        {
            cost = model.CachedCost;
        }
        else
        {
            (long x, long y, long z) = ExMem.ComputeArgSizes(func, args, _variant >= Variant.D);
            cost = model.Eval(x, y, z);
        }

        RemainingBudget -= cost;

        if (_debug is not null)
        {
            int idx = (int)func;
            _debug.BuiltinCpu[idx] = SatMath.Add(_debug.BuiltinCpu[idx], cost.Cpu);
            _debug.BuiltinMem[idx] = SatMath.Add(_debug.BuiltinMem[idx], cost.Mem);
        }

        if (!_measuring && RemainingBudget.IsExhausted)
        {
            _ = Halt(BudgetStop.Builtin);
            return null;
        }
        return func == DefaultFunction.ConsByteString && _variant is Variant.A or Variant.B or Variant.D
            ? Builtins.ByteStringBuiltins.ConsByteStringWrapping(args)
            : Builtins.BuiltinRuntime.Call(func, args);
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
