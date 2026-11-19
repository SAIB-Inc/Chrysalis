using Chrysalis.Plutus.Text;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class CostModelTests
{
    private static readonly ConstTerm<DeBruijn> Unit = new(UnitConstant.Instance);
    // Plutus 1.70.0.0, Cek/Internal.hs:208–219: ascending StepKind order, then startup (not a step).
    private static readonly string[] MachineKinds = ["Const", "Var", "Lam", "Apply", "Delay", "Force", "Builtin", "Constr", "Case", "Startup"];

    [Theory]
    [InlineData(1, 332, 343, 351)]
    [InlineData(2, 332, 343, 351)]
    [InlineData(3, 350, 361, 369)]
    public void NameTablesMatchThePinnedParameterNames(int language, int prefix, int total170, int total171)
    {
        ImmutableArray<string> actual = language switch { 1 => ParamNames.V1, 2 => ParamNames.V2, _ => ParamNames.V3 };
        foreach ((string tag, int total) in new[] { ("1.70.0.0", total170), ("1.71.0.0", total171) })
        {
            string[] names = ReadNames(language, out int deployed, tag);
            Assert.Equal(total, names.Length);
            Assert.Equal(prefix, deployed);
            Assert.Equal(names[..deployed], actual);
        }
    }

    internal static string[] DeployedNames(int language) => ReadNames(language, out int deployed)[..deployed];

    private static string[] ReadNames(int language, out int deployed, string tag = "1.70.0.0")
    {
        using StreamReader reader = new(Golden.Open($"plutus-{tag}/V{language}/ParamName.hs"));
        while (reader.ReadLine() is { } header && header != "data ParamName") { }
        List<string> names = [];
        deployed = -1;
        while (reader.ReadLine() is { } line && !line.TrimStart().StartsWith("deriving", StringComparison.Ordinal))
        {
            if (line.Contains("-- Batch 7", StringComparison.Ordinal))
            {
                deployed = names.Count;
            }

            int comment = line.IndexOf("--", StringComparison.Ordinal);
            string name = (comment < 0 ? line : line[..comment]).Trim(' ', '|', '=');
            if (name.Length > 0)
            {
                names.Add(char.ToLowerInvariant(name[0]) + name[1..].Replace('\'', '-'));
            }
        }

        return [.. names];
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(1, "B")]
    [InlineData(1, "D")]
    [InlineData(2, "A")]
    [InlineData(2, "B")]
    [InlineData(2, "D")]
    [InlineData(3, "E")]
    public void EachArrayPositionBindsItsNamedParameter(int language, string variant)
    {
        Variant kind = Enum.Parse<Variant>(variant);
        const long sentinel = 94137;
        string[] names = DeployedNames(language);
        long[] values = new long[names.Length];
        Dictionary<string, long> baseline = Project(CostModelLoader.FromFlat(values, language, kind));
        HashSet<string> reached = [];
        List<string> failures = [];
        for (int i = 0; i < names.Length; i++)
        {
            values[i] = sentinel;
            Dictionary<string, long> model = Project(CostModelLoader.FromFlat(values, language, kind));
            values[i] = 0;
            string[] changed = [.. model.Keys.Where(k => model[k] != baseline[k])];
            string name = names[i];
            if (kind >= Variant.D && Ignored(name, language))
            {
                if (changed.Length != 0)
                {
                    failures.Add($"{i}: ignored {name} changed {string.Join(", ", changed)}");
                }
                continue;
            }

            string leaf = Leaf(name);
            if (changed.Length == 1 && string.Equals(leaf, changed[0], StringComparison.OrdinalIgnoreCase) && model[leaf] == sentinel)
            {
                Assert.True(reached.Add(leaf), $"Duplicate binding: {leaf}");
            }
            else
            {
                failures.Add($"{i}: {name} should set {leaf}; changed [{string.Join(", ", changed)}]");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} incorrect bindings. First eight:\n{string.Join('\n', failures.Take(8))}");
        Assert.Equal(baseline.Count, reached.Count);
    }

    private static bool Ignored(string name, int language) => name is "divideInteger-cpu-arguments-constant" or "modInteger-cpu-arguments-constant"
        || (language is 1 or 2 && name is "modInteger-memory-arguments-minimum" or "remainderInteger-memory-arguments-minimum");

    private static string Leaf(string name)
    {
        if (name.StartsWith("cek", StringComparison.Ordinal))
        {
            return name.ToUpperInvariant();
        }

        return name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-arguments-", "-", StringComparison.Ordinal)
            .Replace("-arguments", "-constant", StringComparison.Ordinal).ToUpperInvariant();
    }

    private static Dictionary<string, long> Project(CostModel model)
    {
        Dictionary<string, long> leaves = [];
        foreach (DefaultFunction function in Enum.GetValues<DefaultFunction>())
        {
            BuiltinCostModel builtin = model.Builtins[(int)function];
            string name = function.ToString().Replace("_", "", StringComparison.Ordinal);
            AddCosts(name + "-cpu", builtin.Cpu);
            AddCosts(name + "-memory", builtin.Mem);
        }

        for (int i = 0; i < MachineKinds.Length; i++)
        {
            ExBudget cost = i == MachineKinds.Length - 1 ? model.Machine.Startup : model.Machine.Steps[i];
            leaves.Add($"cek{MachineKinds[i]}Cost-exBudgetCPU".ToUpperInvariant(), cost.Cpu);
            leaves.Add($"cek{MachineKinds[i]}Cost-exBudgetMemory".ToUpperInvariant(), cost.Mem);
        }
        return leaves;

        void AddCosts(string path, CostFunction function)
        {
            foreach (PropertyInfo property in function.GetType().GetProperties())
            {
                switch (property.GetValue(function))
                {
                    case long value: leaves.Add((path + "-" + property.Name).ToUpperInvariant(), value); break;
                    case CostFunction inner: AddCosts(path + "-" + property.Name, inner); break;
                    default: break;
                }
            }
        }
    }

    [Fact]
    public void CostFormulasFollowUpstream()
    {
        const long max = long.MaxValue;
        Assert.Equal(5, new SubtractedSizesCost(2, 3, 1).Eval(1, 1, 0));
        Assert.Equal(max, new SubtractedSizesCost(0, 1, 0).Eval(max, -1, 0));
        Assert.Equal(1, new LiteralInYOrLinearInZCost(4, 2).Eval(1, 1, 1));
        Assert.Equal(6, new LiteralInYOrLinearInZCost(4, 2).Eval(1, 0, 1));
        Assert.Equal(7, new QuadraticInXAndYCost(7, -100, 0, 0, 0, 0, 0).Eval(1, 1, 0));
        Assert.Equal(max - 1, new QuadraticInXAndYCost(0, max, 0, 1, -1, 0, 0).Eval(1, 1, 0));
        Assert.Equal(max - 1, new ExpModCost(max, 1, -1).Eval(1, 1, 1));
        Assert.Equal(-5, new ExpModCost(-3, 0, 0).Eval(2, 1, 1));
        Assert.Equal(max - 1, new WithInteractionCost(max, 1, -1, -1).Eval(1, 1, 0));
        Assert.Equal(max, QuadraticCost.InX(max, 1, -1).Eval(1, 0, 0));
        Assert.Equal(-2, SatMath.DivFloor(-3, 2));
        Assert.Equal(max, SatMath.DivFloor(max, 2));
        Assert.Equal(long.MinValue, SatMath.DivFloor(long.MinValue, 2));
        Assert.Equal(max, new LinearInYAndZCost(max, 1, -1).Eval(0, 1, 1));
        Assert.Equal(max, new LinearInXAndYCost(max, 1, -1).Eval(1, 1, 0));
    }

    [Theory]
    [InlineData("B", true, 97, 15, 13)]
    [InlineData("C", false, 97, 17, 16)]
    [InlineData("D", true, 13, 15, 13)]
    [InlineData("E", false, 16, 17, 16)]
    public void DivisionFamilyUsesTheVariantShape(string variant, bool multiplied, long below, long on, long above)
    {
        Variant kind = Enum.Parse<Variant>(variant);
        string[] names = DeployedNames(multiplied ? 1 : 3);
        long[] values = new long[names.Length];
        foreach (DefaultFunction f in new[] { DefaultFunction.DivideInteger, DefaultFunction.QuotientInteger, DefaultFunction.RemainderInteger, DefaultFunction.ModInteger })
        {
            string name = char.ToLowerInvariant(f.ToString()[0]) + f.ToString()[1..];
            values[Array.IndexOf(names, name + "-cpu-arguments-constant")] = 97;
            string inner = name + "-cpu-arguments-model-arguments-";
            values[Array.IndexOf(names, inner + (multiplied ? "intercept" : "c00"))] = 7;
            values[Array.IndexOf(names, inner + (multiplied ? "slope" : "c10"))] = 2;
            if (!multiplied)
            {
                values[Array.IndexOf(names, inner + "c01")] = 3;
            }

            values[Array.IndexOf(names, name + "-memory-arguments-intercept")] = 2;
            values[Array.IndexOf(names, name + "-memory-arguments-slope")] = 3;
            int min = Array.IndexOf(names, name + "-memory-arguments-minimum");
            bool subtracted = kind == Variant.B || f is DefaultFunction.DivideInteger or DefaultFunction.QuotientInteger;
            if (min >= 0)
            {
                values[min] = subtracted ? 1 : 23;
            }
        }

        CostModel model = CostModelLoader.Build(values, [.. names], kind);
        foreach (DefaultFunction f in new[] { DefaultFunction.DivideInteger, DefaultFunction.QuotientInteger, DefaultFunction.RemainderInteger, DefaultFunction.ModInteger })
        {
            BuiltinCostModel cost = model.Builtins[(int)f];
            bool subtracted = kind == Variant.B || f is DefaultFunction.DivideInteger or DefaultFunction.QuotientInteger;
            long lowerCpu = f is DefaultFunction.QuotientInteger or DefaultFunction.RemainderInteger ? 97 : below;
            Assert.Equal(new ExBudget(lowerCpu, subtracted ? 5 : 11), cost.Eval(1, 3, 0));
            Assert.Equal(new ExBudget(on, subtracted ? 5 : 8), cost.Eval(2, 2, 0));
            Assert.Equal(new ExBudget(above, subtracted ? 8 : 5), cost.Eval(3, 1, 0));
        }
    }

    [Fact]
    public void VariantAUsesAddedIntegerSizesAndTheSignatureLength()
    {
        string[] names = DeployedNames(1);
        long[] values = new long[names.Length];
        values[Array.IndexOf(names, "multiplyInteger-cpu-arguments-intercept")] = 7;
        values[Array.IndexOf(names, "multiplyInteger-cpu-arguments-slope")] = 11;
        values[Array.IndexOf(names, "verifyEd25519Signature-cpu-arguments-intercept")] = 13;
        values[Array.IndexOf(names, "verifyEd25519Signature-cpu-arguments-slope")] = 17;
        CostModel model = CostModelLoader.FromFlat(values, 1, Variant.A);
        Assert.Equal(62, model.Builtins[(int)DefaultFunction.MultiplyInteger].Eval(2, 3, 0).Cpu);
        Assert.Equal(98, model.Builtins[(int)DefaultFunction.VerifyEd25519Signature].Eval(2, 3, 5).Cpu);
    }

    [Fact]
    public void ShortLongAndHostileArrays()
    {
        foreach ((int language, int length) in new[] { (3, 296), (3, 297), (1, 166) })
        {
            string[] names = DeployedNames(language);
            long[] costs = [.. Enumerable.Repeat(1L, length)];
            CostModel shortModel = CostModelLoader.FromFlat(costs, language, language == 3 ? Variant.E : Variant.D);
            Dictionary<string, long> leaves = Project(shortModel);
            for (int i = 0; i < names.Length; i++)
            {
                if (!Ignored(names[i], language))
                {
                    Assert.Equal(i < length ? 1L : long.MaxValue, leaves[Leaf(names[i])]);
                }
            }
            Assert.Equal(long.MaxValue, shortModel.Builtins[(int)DefaultFunction.ExpModInteger].Eval(1, 1, 1).Mem);
        }

        int minimum = Array.IndexOf(DeployedNames(1), "remainderInteger-memory-arguments-minimum");
        CostModel ignoredMinimum = CostModelLoader.FromFlat([.. Enumerable.Repeat(1L, minimum)], 1, Variant.D);
        Assert.Equal(1, ignoredMinimum.Builtins[(int)DefaultFunction.RemainderInteger].Eval(1, 0, 1).Mem);

        long[] oversized = [.. Enumerable.Range(1, 400).Select(i => (long)i)];
        CostModel copied = CostModelLoader.FromFlat(oversized, 3, Variant.E);
        oversized[350] = long.MinValue;
        Assert.Equal(Project(copied), Project(CostModelLoader.FromFlat(oversized[..350], 3, Variant.E)));
        oversized[0] = long.MaxValue;
        Assert.Equal(1, Project(copied)[Leaf(DeployedNames(3)[0])]);

        foreach (long hostile in new[] { long.MaxValue, long.MinValue })
        {
            CostModel model = CostModelLoader.FromFlat([.. Enumerable.Repeat(hostile, 350)], 3, Variant.E);
            ulong expected = hostile > 0 ? (ulong)long.MaxValue : 0;
            Assert.Equal(new ExUnitsResult(expected, expected), Evaluator.Run(Unit, model, 3, null).ExUnits);
            ApplyTerm<DeBruijn> applied = new(new LambdaTerm<DeBruijn>(new(0), Unit), Unit);
            Assert.Equal(new ExUnitsResult(expected, expected), Evaluator.Run(applied, model, 3, null).ExUnits);
        }
        string[] v3Names = DeployedNames(3);
        long[] twice = new long[v3Names.Length];
        twice[Array.IndexOf(v3Names, "cekConstCost-exBudgetCPU")] = 1L << 62;
        CostModel twoConst = CostModelLoader.FromFlat(twice, 3, Variant.E);
        Assert.Equal((ulong)long.MaxValue, Evaluator.Run(new ApplyTerm<DeBruijn>(new LambdaTerm<DeBruijn>(new(0), Unit), Unit), twoConst, 3, null).ExUnits.Steps);
        Assert.Equal(new ExBudget(long.MaxValue, long.MinValue), new ExBudget(long.MaxValue, long.MinValue) + new ExBudget(1, -1));
        Assert.Equal(new ExBudget(long.MaxValue, long.MinValue), ExBudget.Zero - new ExBudget(long.MinValue, long.MaxValue) - new ExBudget(0, 2));
    }

    [Theory]
    [InlineData("replicateByte", "(con integer 1180591620717411303424) (con integer 0)")]
    [InlineData("integerToByteString", "(con bool True) (con integer 1180591620717411303424) (con integer 0)")]
    public void WidthOfTwoToTheSeventySaturatesBuiltinMemory(string builtin, string args)
    {
        Term<DeBruijn> term = UplcParser.NameToDeBruijn(UplcParser.Parse($"(program 1.0.0 [(builtin {builtin}) {args}])")).Term;
        CostModel model = CostModelLoader.FromFlat(NodeBudgetTests.Fixture.Models[2], 3, Variant.E);
        CekMachine machine = new CekMachine(null, model).EnableDebugCounters();
        _ = Assert.Throws<EvaluationException>(() => machine.RunValue(term)); // width exceeds the builtin's output limit
        Assert.Equal(long.MaxValue, machine.DebugCounters.BuiltinMem![(int)Enum.Parse<DefaultFunction>(builtin, ignoreCase: true)]);
    }

    [Fact]
    public void PlutusV4UsesTheV3ChainModel()
    {
        CostModel v3 = CostModelLoader.FromFlat(DefaultCosts.LatestV3, 3, Variant.E);
        CostModel v4 = CostModelLoader.FromFlat(DefaultCosts.LatestV3, 4, Variant.E);
        Assert.Equal(Variant.E, v4.Variant);
        Assert.Equal(Project(v3), Project(v4));
    }

    [Fact]
    public void MachineStepsSpendByKindInOrder()
    {
        VarTerm<DeBruijn> variable = new(new(1));
        LambdaTerm<DeBruijn> identity = new(new(0), variable);
        LambdaTerm<DeBruijn> ignore = new(new(0), Unit);
        ApplyTerm<DeBruijn> applied = new(identity, Unit);
        ConstrTerm<DeBruijn> empty = new(0, []);
        DelayTerm<DeBruijn> delay = new(Unit);
        ConstrTerm<DeBruijn> script = new(0,
        [
            .. Enumerable.Repeat(Unit, 25), .. Enumerable.Repeat(applied, 26),
            .. Enumerable.Repeat(delay, 27), .. Enumerable.Repeat(new ForceTerm<DeBruijn>(delay), 28),
            .. Enumerable.Repeat(new BuiltinTerm<DeBruijn>(DefaultFunction.AddInteger), 29),
            .. Enumerable.Repeat(empty, 30), .. Enumerable.Repeat(new CaseTerm<DeBruijn>(empty, [Unit]), 31),
            ignore, ignore, ignore, new ApplyTerm<DeBruijn>(ignore, Unit)
        ]);
        int[] counts = [112, 26, 30, 27, 55, 28, 29, 62, 31];
        ImmutableArray<ExBudget> steps = [.. Enumerable.Range(1, 9).Select(i => new ExBudget(10 * i, 11 - i))];
        ExBudget startup = new(11, 17);
        CostModel varied = CostModel.Default with { Machine = new(startup, steps) };
        ExBudget initial = ExBudget.Unlimited;
        CekMachine machine = new CekMachine(null, varied).EnableDebugCounters();
        _ = machine.Run(script);
        Assert.Equal(counts, machine.DebugCounters.Steps);
        ExBudget expected = steps.Zip(counts).Aggregate(startup, (spent, pair) => spent + new ExBudget(pair.First.Cpu * pair.Second, pair.First.Mem * pair.Second));
        Assert.Equal(new ExBudget(initial.Cpu - expected.Cpu, initial.Mem - expected.Mem), machine.RemainingBudget);

        CostModel order = varied with { Machine = new(ExBudget.Zero, [new(-10, 0), default, new(10, 0), default, default, default, default, default, default]) };
        CekMachine ordered = new(null, order);
        _ = ordered.Run(applied);
        Assert.Equal(10UL, Evaluator.UsedUnits(initial, ordered.RemainingBudget).Steps);
    }

    [Fact]
    public void MachinesShareOneImmutableDefault()
    {
        BuiltinCostModel[] independent = DefaultCosts.Create();
        _ = Measure(); // Warm up the constructors and the allocation counter.
        (long ordinary, long prebuilt) = Measure();
        Assert.Equal(prebuilt, ordinary);

        Term<DeBruijn> add = new ApplyTerm<DeBruijn>(new ApplyTerm<DeBruijn>(
            new BuiltinTerm<DeBruijn>(DefaultFunction.AddInteger), new ConstTerm<DeBruijn>(new IntegerConstant(1))),
            new ConstTerm<DeBruijn>(new IntegerConstant(2)));
        ExUnitsResult before = Evaluator.EvaluateTerm(add).ExUnits;
        independent[(int)DefaultFunction.AddInteger] = new(new ConstantCost(0), new ConstantCost(0));
        Assert.Equal(before, Evaluator.EvaluateTerm(add).ExUnits);
        ExUnitsResult[] results = new ExUnitsResult[16];
        _ = Parallel.For(0, results.Length, i =>
        {
            CekMachine machine = new(ExBudget.Unlimited, CostModel.Default);
            _ = machine.Run(add);
            results[i] = Evaluator.UsedUnits(ExBudget.Unlimited, machine.RemainingBudget);
        });
        Assert.All(results, result => Assert.Equal(before, result));

        [MethodImpl(MethodImplOptions.NoInlining)]
        (long Ordinary, long Prebuilt) Measure()
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new CekMachine());
            long middle = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new CekMachine(ExBudget.Unlimited, independent));
            return (middle - start, GC.GetAllocatedBytesForCurrentThread() - middle);
        }
    }
}
