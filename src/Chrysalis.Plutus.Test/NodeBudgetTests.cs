using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Text;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed partial class NodeBudgetTests
{
    internal sealed record Script(string Id, int Language, string Purpose, byte[] Flat, ExUnitsResult? Units, ExUnitsResult? Budget);

    internal sealed record SerialiseCase(string Id, byte[] Flat, byte[] Bytes);

    internal sealed record NodeData(long[][] Models, Script[] Scripts, (Script Script, string Outcome)[] Failures, SerialiseCase[] Serialise);

    internal static NodeData Fixture => Data.Value;

    private static readonly int[] NegativeV3Indexes = [52, 117, 133, 144];
    private static readonly int[] MeasuredCounts = [69, 161, 514];
    private static readonly Lazy<NodeData> Data = new(Load);
    internal sealed record KnownFailure(string Owner, string Stage, string ExceptionType, string MessageSha256, string Reason)
    {
        internal bool Matches(string stage, Exception error) =>
            stage == Stage && error.GetType().FullName == ExceptionType &&
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(error.Message))) == MessageSha256;
    }

    internal static Dictionary<string, KnownFailure> ReadKnownFailures()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "NodeBudgetKnownFailures.tsv");
        using StreamReader reader = File.OpenText(path);
        Assert.Equal("id\towner\tstage\texception\tmessageSha256\treason", reader.ReadLine());
        Dictionary<string, KnownFailure> failures = [];
        while (reader.ReadLine() is { } line)
        {
            string[] columns = line.Split('\t');
            Assert.True(columns is [_, "V3" or "V4" or "V6", "decode" or "evaluate", _, _, _], $"Malformed known failure: {line}");
            failures.Add(columns[0], new(columns[1], columns[2], columns[3], columns[4], columns[5]));
        }
        return failures;
    }

    private static NodeData Load()
    {
        using GZipStream stream = new(Golden.Open("plutigo-v0.12.3/node_ground_truth_pv11.json.gz"), CompressionMode.Decompress);
        using JsonDocument doc = JsonDocument.Parse(stream);
        Assert.Equal(11, doc.RootElement.GetProperty("protocolMajor").GetInt32());
        long[][] models = ReadModels(doc.RootElement);
        (Script Script, string Outcome)[] rows = [.. doc.RootElement.GetProperty("scripts").EnumerateArray()
            .Select(e => (new Script(e.GetProperty("id").GetString()!, ReadLanguage(e), e.GetProperty("purpose").GetString()!,
                Convert.FromHexString(e.GetProperty("flat").GetString()!), Units(e, "exunits"), Units(e, "budget")), e.GetProperty("outcome").GetString()!))];
        SerialiseCase[] serialise = [.. doc.RootElement.GetProperty("serialise").EnumerateArray().Select(e =>
            new SerialiseCase(e.GetProperty("id").GetString()!, Convert.FromHexString(e.GetProperty("flat").GetString()!),
                Convert.FromHexString(e.GetProperty("bytes").GetString()!)))];
        return new(models, [.. rows.Where(row => row.Outcome == "ok").Select(row => row.Script)],
            [.. rows.Where(row => row.Outcome is "phase1-reject" or "phase2-fail")], serialise);
    }

    private static int ReadLanguage(JsonElement entry) => entry.GetProperty("lang").GetString() switch
    {
        "V1" => 1,
        "V2" => 2,
        "V3" => 3,
        _ => throw new InvalidDataException("Unexpected fixture language")
    };

    private static ExUnitsResult? Units(JsonElement script, string key) =>
        script.TryGetProperty(key, out JsonElement units) ? new(units.GetProperty("mem").GetUInt64(), units.GetProperty("steps").GetUInt64()) : null;

    private static long[][] ReadModels(JsonElement root) => [.. Enumerable.Range(1, 3).Select(i =>
        (long[])[.. root.GetProperty("costModels").GetProperty($"PlutusV{i}").EnumerateArray().Select(x => x.GetInt64())])];

    [Fact]
    public void RecordedScriptsMatchTheNode()
    {
        NodeData fixture = Fixture;
        using Stream parameters = Golden.Open("g1/current-protocol-parameters.json");
        using JsonDocument g1 = JsonDocument.Parse(parameters);
        long[][] g1Models = ReadModels(g1.RootElement);
        Assert.Equal(g1Models[2], DefaultCosts.LatestV3);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(g1Models[i], fixture.Models[i]);
            Assert.Equal(i == 2 ? NegativeV3Indexes : [], NegativeIndexes(fixture.Models[i]));
        }
        Assert.Equal(NegativeV3Indexes, NegativeIndexes(DefaultCosts.VariantC));

        Script[] scripts = fixture.Scripts;
        Assert.Equal(745, scripts.Length);
        Assert.Equal(MeasuredCounts, scripts.Where(s => s.Units is not null).GroupBy(s => s.Language).OrderBy(g => g.Key).Select(g => g.Count()));
        Script exact = Assert.Single(scripts, s => s.Units is null);
        Script twin = scripts.Single(s => s.Id == "P-B01-i2b-size-8192");
        Assert.Equal("X-i2b-exact", exact.Id);
        Assert.Equal((twin.Language, twin.Purpose), (exact.Language, exact.Purpose));
        Assert.Equal(twin.Flat, exact.Flat);
        Assert.Equal(new ExUnitsResult(2424, 1530707), twin.Units);
        Assert.Equal(twin.Units, exact.Budget);

        Dictionary<string, KnownFailure> known = ReadKnownFailures();
        CostModel[] models = [.. fixture.Models.Select((a, i) => CostModelLoader.FromFlat(a, i + 1, i == 2 ? Variant.E : Variant.D))];
        string?[] mismatches = new string?[scripts.Length];
        _ = Parallel.For(0, scripts.Length, i =>
        {
            Script script = scripts[i];
            ExUnitsResult expected = (script.Units ?? script.Budget)!;
            _ = known.TryGetValue(script.Id, out KnownFailure? failure);
            try
            {
                ExUnitsResult actual = EvaluateRecordedScript(script).ExUnits;
                if (failure is not null)
                {
                    mismatches[i] = $"{script.Id}: stale {failure.Owner} entry; evaluated with {Show(actual)}";
                }
                else if (actual != expected)
                {
                    mismatches[i] = $"{script.Id} V{script.Language}: expected {Show(expected)}; got {Show(actual)}";
                }
            }
            catch (Exception ex)
            {
                string? type = ex.GetType().FullName;
                bool matches = failure is not null && (failure.Stage == "decode"
                    ? Record.Exception(() => FlatDecoder.DecodeProgram(script.Flat)) is { } decode && failure.Matches("decode", decode) && failure.Matches("decode", ex)
                    : failure.Matches("evaluate", ex));
                if (!matches)
                {
                    mismatches[i] = $"{script.Id} V{script.Language}: unexpected replay failure: {type}: {ex.Message}";
                }
            }
        });
        Assert.All(known.Keys, id => Assert.True(scripts.Any(s => s.Id == id) || fixture.Serialise.Any(s => s.Id == id), $"Unknown known-failure id: {id}"));
        string[] phase1 = ["AV-multiIndexArray-V1", "AV-multiIndexArray-V2", "AV-multiIndexArray-V3", "F-ver-1.0.0-case-V3"];
        string[] phase2 = ["F-ver-0.1.0-V3", "F-ver-1.0.1-V3", "F-ver-1.1.0-patch-2^70-V3", "F-ver-1.2.0-V3", "F-ver-1.2^32.0-V3",
            "F-ver-2.0.0-V3", "F-ver-2^64.0.0-V3", "F-ver-2^64.0.0-V2",
            "P-C6-case-bool-true-one-branch", "P-C8-case-list-nil-one-branch"];
        string[] unboundScope = ["O-unforced-delay-outside-case-V1", "O-unforced-delay-outside-case-V2",
            "O-unforced-delay-outside-case-V3", "P-A6-free-var-under-unused-lambda"];
        string[] unboundReached = ["O-taken-branch-V1", "O-taken-branch-V2", "O-taken-branch-V3",
            "P-A5-free-var-taken-case-branch", "O-evaluated-constr-field-V1", "O-evaluated-constr-field-V2",
            "O-evaluated-constr-field-V3"];
        string[] v4Prefixes = ["BI-", "BB-", "BLS-", "D-", "SER-", "EXP-", "S-", "W-", "R-"];
        HashSet<string> v4Ids =
        [
            "P-B11-writeBits-4097", "P-B12-shift-2^63", "P-B13-shift-neg-2^64", "P-B14-rotate-2^64+1",
            "P-B17-addInteger-arg-2^262143", "P-B18-addInteger-arg-2^262143-1", "P-B20-sha2-65537-bytes",
            "P-B23-constrData-tag-2^64", "P-B24-constrData-tag-neg", "P-B30-scalarMul-2^4095",
            "F-var-10th-group-2-V3", "F-var-10th-group-cont-V3", "F-var-11groups-V1", "F-var-11groups-V3",
            "F-constr-tag-10th-group-2-V3", "F-constr-tag-11groups-V3", "F-constr-tag-2^32-evaluated-V3", "F-constr-tag-2^63-evaluated-V3",
            "P-C2-type-33-tags", "P-C4-constr-1025-fields", "P-A1-zero-width-list-pair4-x124000", "P-A1-zero-width-list-pair5-x124000",
            "Z-tags33-V1-spend", "Z-tags33-V2-spend", "Z-tags33-V3-spend",
            "P-B34-expMod-noninvertible", "P-B36-expMod-m0", "P-B37-expMod-m-neg", "P-B38-expMod-m-2^8191",
            "P-B39-expMod-b0-eneg", "P-B40-expMod-base-oob", "P-B48-slice-skip-2^63",
            "P-B09-replicateByte-byte-256", "P-B15-readBit-2^63", "P-B26-msm-scalar-2^4095", "P-B28-msm-scalar--2^4095-1"
        ];
        (Script Script, string Outcome)[] v4 = [.. fixture.Failures.Where(row =>
            v4Prefixes.Any(prefix => row.Script.Id.StartsWith(prefix, StringComparison.Ordinal)) || v4Ids.Contains(row.Script.Id))];
        Assert.Equal(417, v4.Length);
        string[] errors = [.. mismatches.OfType<string>(),
            .. CheckVerdicts([.. phase1, .. v4.Where(row => row.Outcome == "phase1-reject").Select(row => row.Script.Id)], "phase1-reject", typeof(InvalidOperationException)),
            .. CheckVerdicts([.. phase2, .. v4.Where(row => row.Outcome == "phase2-fail" && !row.Script.Id.StartsWith("R-", StringComparison.Ordinal)).Select(row => row.Script.Id)], "phase2-fail", typeof(EvaluationException)),
            .. CheckVerdicts([.. v4.Where(row => row.Script.Id.StartsWith("R-", StringComparison.Ordinal)).Select(row => row.Script.Id)], "phase2-fail", typeof(InvalidReturnValueException)),
            .. CheckVerdicts(unboundScope, "phase2-fail", typeof(UnboundVariableException)),
            .. CheckVerdicts(unboundReached, "phase2-fail", typeof(EvaluationException))];
        Assert.True(errors.Length == 0, $"{errors.Length} node replay mismatches:\n{string.Join('\n', errors)}");

        string[] openAccepted = [.. scripts.Where(s => s.Id.StartsWith("O-", StringComparison.Ordinal)
            || s.Id is "P-A3-free-var-untaken-case-branch" or "P-A4-free-var-unevaluated-constr-field"
                or "F-var-2^63+1-V3" or "F-var-2^63-10groups-V3" or "F-var-2^64-1-V2" or "F-var-2^64-1-V3").Select(s => s.Id)];
        Assert.Equal(30, openAccepted.Length);
        Assert.All(openAccepted, id => Assert.DoesNotContain(id, known.Keys));

        string[] stringCases = [.. scripts.Where(s => s.Id.StartsWith("W-builtin_semantics_", StringComparison.Ordinal))
            .Where(s => s.Id.Contains("appendString", StringComparison.Ordinal) || s.Id.Contains("encodeUtf8", StringComparison.Ordinal)
                || s.Id.Contains("equalsString", StringComparison.Ordinal)).Select(s => s.Id)];
        Assert.Equal(4, stringCases.Length);
        Assert.All(stringCases, id => Assert.DoesNotContain(id, known.Keys));

        CheckWitness("EXP-e0-V1", new(1701, 1148094), "VS-valueData-1x1-V1", DefaultFunction.ValueData);
        CheckWitness("BI-expMod-mod-hugeexp-control-V2", new(1701, 1148094), "VAL-insertCoin-positive-empty-V2", DefaultFunction.InsertCoin);
        CheckWitness("EXP-e0-V3", new(1401, 1100094), "L-dropList-head-2000", DefaultFunction.DropList);

        EvaluationResult EvaluateRecordedScript(Script script) => Evaluator.EvaluateScript(script.Flat,
            Enumerable.Repeat<PlutusData>(new PlutusDataConstr(0, []), ArgumentCount(script.Language, script.Purpose)).ToArray(),
            script.Language, 11, fixture.Models[script.Language - 1]);

        IEnumerable<string> CheckVerdicts(string[] ids, string outcome, Type expected)
        {
            string? expectedMessage = expected == typeof(UnboundVariableException) ? new UnboundVariableException().Message : null;
            foreach (string id in ids)
            {
                (Script script, string recorded) = fixture.Failures.Single(row => row.Script.Id == id);
                Exception? observed = Record.Exception(() => EvaluateRecordedScript(script));
                if (recorded != outcome || observed?.GetType() != expected
                    || (expectedMessage is not null && observed.Message != expectedMessage))
                {
                    yield return $"{id}: expected {outcome}/{expected.Name}; fixture {recorded}, got {observed?.GetType().Name ?? "success"}: {observed?.Message}";
                }
            }
        }

        void CheckWitness(string primary, ExUnitsResult budget, string alternate, DefaultFunction alternateFunction)
        {
            Script witness = scripts.Single(s => s.Id == primary);
            Assert.Equal(budget, witness.Units);
            DefaultFunction function = DefaultFunction.ExpModInteger;
            if (known.ContainsKey(primary))
            {
                witness = scripts.Single(s => s.Id == alternate);
                function = alternateFunction;
            }
            Assert.DoesNotContain(witness.Id, known.Keys);
            Term<DeBruijn> term = ApplyLedgerArgs(witness, FlatDecoder.DecodeProgram(witness.Flat).Term);
            CostModel model = models[witness.Language - 1];
            (ExUnitsResult used, long cpu, long mem) = WithCounters(term, model, function);
            Assert.Equal(witness.Units, used);
            Assert.True(cpu > 0);
            long[] changed = (long[])fixture.Models[witness.Language - 1].Clone();
            string name = function.ToString();
            string parameter = char.ToLowerInvariant(name[0]) + name[1..] + "-memory-arguments" +
                (function == DefaultFunction.DropList ? "" : "-intercept");
            changed[Array.IndexOf(CostModelTests.DeployedNames(witness.Language), parameter)]++;
            (ExUnitsResult raised, _, long raisedMem) = WithCounters(term, CostModelLoader.FromFlat(changed, witness.Language, witness.Language == 3 ? Variant.E : Variant.D), function);
            Assert.True(raisedMem > mem);
            Assert.Equal(used.Mem + (ulong)(raisedMem - mem), raised.Mem);
            Assert.Equal(used.Steps, raised.Steps);
        }
    }

    [Theory]
    [InlineData("P-B11-writeBits-4097", false)]
    [InlineData("P-B12-shift-2^63", false)]
    [InlineData("P-B13-shift-neg-2^64", false)]
    [InlineData("P-B14-rotate-2^64+1", false)]
    [InlineData("P-B17-addInteger-arg-2^262143", false)]
    [InlineData("P-B20-sha2-65537-bytes", false)]
    [InlineData("P-B23-constrData-tag-2^64", false)]
    [InlineData("P-B24-constrData-tag-neg", false)]
    [InlineData("P-B30-scalarMul-2^4095", false)]
    [InlineData("P-C2-type-33-tags", false)]
    [InlineData("P-C4-constr-1025-fields", false)]
    [InlineData("Z-tags33-V3-spend", false)]
    [InlineData("BI-addInteger-arg0-2^262143-V1", false)]
    [InlineData("BI-consByteString-arg0-2^262143-V1", false)]
    [InlineData("BB-sha2_256-arg0-65537-V1", false)]
    [InlineData("Z-tags33-V1-spend", false)]
    [InlineData("P-B48-slice-skip-2^63", true)]
    [InlineData("S-skip2^63-V1", true)]
    public void BoundsAndLimitsStartAtVanRossem(string id, bool throwsAtPv10)
    {
        NodeData fixture = Fixture;
        Script script = fixture.Failures.Single(row => row.Script.Id == id).Script;
        PlutusData[] args = [.. Enumerable.Repeat<PlutusData>(new PlutusDataConstr(0, []), ArgumentCount(script.Language, script.Purpose))];
        long[] model = fixture.Models[script.Language - 1];
        if (throwsAtPv10)
        {
            _ = Assert.Throws<EvaluationException>(() => Evaluator.EvaluateScript(script.Flat, args, script.Language, 10, model));
        }
        else
        {
            Assert.True(Evaluator.EvaluateScript(script.Flat, args, script.Language, 10, model).ExUnits.Steps > 0);
        }
    }

    private static int[] NegativeIndexes(IReadOnlyList<long> values) => [.. Enumerable.Range(0, values.Count).Where(i => values[i] < 0)];

    private static string Show(ExUnitsResult units) => FormattableString.Invariant($"{units.Mem} / {units.Steps}");

    private static int ArgumentCount(int language, string purpose) => language == 3 ? 1 : purpose == "spend" ? 3 : 2;

    private static Term<DeBruijn> ApplyLedgerArgs(Script script, Term<DeBruijn> term) =>
        ApplyLedgerArgs(script.Language, script.Purpose, term);

    private static Term<DeBruijn> ApplyLedgerArgs(int language, string purpose, Term<DeBruijn> term)
    {
        // plutigo node_ground_truth_test.go:97–110: recorded scripts ignore their arguments.
        int nargs = ArgumentCount(language, purpose);
        ConstTerm<DeBruijn> arg = new(new DataConstant(new PlutusDataConstr(0, [])));
        for (int i = 0; i < nargs; i++)
        {
            term = new ApplyTerm<DeBruijn>(term, arg);
        }
        return term;
    }

    private static (ExUnitsResult Used, long Cpu, long Mem) WithCounters(Term<DeBruijn> term, CostModel model, DefaultFunction function)
    {
        CekMachine machine = new CekMachine(null, model).EnableDebugCounters();
        _ = machine.RunValue(term);
        (_, long[]? cpu, long[]? mem) = machine.DebugCounters;
        return (Evaluator.UsedUnits(ExBudget.Unlimited, machine.RemainingBudget), cpu![(int)function], mem![(int)function]);
    }

    // Plutus 1.70.0.0, Builtins.hs's Flat DefaultFun instance (also checked with aiken uplc decode).
    [Theory]
    [InlineData("0100007741", DefaultFunction.Bls12_381_G1_Compress, 1)]
    [InlineData("0100007761", DefaultFunction.Bls12_381_G1_Uncompress, 1)]
    [InlineData("0100007781", DefaultFunction.Bls12_381_G1_HashToGroup, 2)]
    [InlineData("0100007821", DefaultFunction.Bls12_381_G2_Compress, 1)]
    [InlineData("0100007841", DefaultFunction.Bls12_381_G2_Uncompress, 1)]
    [InlineData("0100007861", DefaultFunction.Bls12_381_G2_HashToGroup, 2)]
    public void BuiltinTagsFollowTheFlatEncoding(string hex, DefaultFunction function, int arity)
    {
        byte[] bytes = Convert.FromHexString(hex);
        Program<DeBruijn> program = FlatDecoder.DecodeProgram(bytes);
        Assert.Equal(function, Assert.IsType<BuiltinTerm<DeBruijn>>(program.Term).Function);
        Assert.Equal(bytes, FlatEncoder.EncodeProgram(new(program.Version, new BuiltinTerm<DeBruijn>(function))));
        Assert.Equal(arity, function.Arity());
    }

    private sealed record BudgetCase(string Id, int Language, string Purpose, byte[] Flat, string Outcome,
        ExBudget Budget, ExBudget? Remaining, string? EvaluationError)
    {
        internal Term<DeBruijn> Apply(Term<DeBruijn> term) => ApplyLedgerArgs(Language, Purpose, term);
        internal Term<DeBruijn> Decode() => FlatDecoder.DecodeProgram(Flat).Term;
    }

    private sealed record BudgetData(long[][] Models, BudgetCase[] Cases);
    private static readonly Lazy<BudgetData> Failures = new(LoadBudgetData);

    private static BudgetData LoadBudgetData()
    {
        using GZipStream stream = new(Golden.Open("plutigo-v0.12.3/node_budget_failures_pv11.json.gz"), CompressionMode.Decompress);
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;
        Assert.Equal(11, root.GetProperty("protocolMajor").GetInt32());
        BudgetCase[] cases = [.. root.GetProperty("cases").EnumerateArray().Select(e => new BudgetCase(
            e.GetProperty("id").GetString()!, ReadLanguage(e), e.GetProperty("purpose").GetString()!,
            Convert.FromHexString(e.GetProperty("flat").GetString()!), e.GetProperty("outcome").GetString()!,
            ReadBudget(e.GetProperty("budget")), e.TryGetProperty("remaining", out JsonElement remaining) ? ReadBudget(remaining) : null,
            e.TryGetProperty("evaluationError", out JsonElement error) ? error.GetString() : null))];
        return new(ReadModels(root), cases);
    }

    private static ExBudget ReadBudget(JsonElement units) => new(units.GetProperty("steps").GetInt64(), units.GetProperty("mem").GetInt64());

    private static BudgetCase FindBudgetCase(string id) => Failures.Value.Cases.Single(c => c.Id == id);

    // Startup, prior spends and whole builtin charges from the node arrays and sibling stops, using Plutus 1.70.0.0's
    // ExMemoryUsage.hs:221–227, Core.hs:318–321 and CostStream.hs:94–106. Never derived from CEK output.
    private static readonly (string Family, long Mem, long Cpu)[] Aggregates =
    [
        ("B-eqdata-list", 101, 5099214),
        ("B-eqdata-tree", 101, 4062612),
        ("B-l2a-250", 357, 6210600),
        ("B-ser-list40", 508, 44471254),
        ("B-ser-tree", 528, 46604374),
        ("B3-eqdata-bigint-vs-list", 101, 1443828),
        ("B3-eqdata-constr-vs-list", 101, 1416549),
        ("B3-eqdata-list-vs-constr", 101, 1416549),
        ("B3-eqdata-map-vs-list", 101, 2807778),
        ("B3-l2a-100", 207, 2484900),
        ("B3-l2a-101", 208, 2509738),
        ("B3-l2a-199", 306, 4943862),
        ("B3-l2a-200", 307, 4968700),
        ("B3-l2a-201", 308, 4993538),
        ("B3-l2a-350", 457, 8694400),
        ("B3-msmg1-100", 168, 2883624928),
        ("B3-msmg1-150", 168, 4138008378),
        ("B3-ser-map", 240, 15887446),
        ("B3-unvaluedata", 442, 2975984),
        ("B4-ser-V1", 408, 33805654),
        ("B4-ser-V2", 408, 33805654),
        ("B4-writebits-100", 104, 2166045),
        ("B4-writebits-250", 104, 4993245)
    ];

    private static ExBudget? PartialBuiltinAggregate(BudgetCase entry)
    {
        if (entry.Remaining is not ExBudget nodeRemaining || entry.Budget is not { Cpu: >= 100, Mem: >= 100 })
        {
            return null;
        }
        foreach ((string family, long mem, long cpu) in Aggregates)
        {
            if (entry.Id.StartsWith(family + "-", StringComparison.Ordinal))
            {
                ExBudget nodeUsed = entry.Budget - nodeRemaining;
                ExBudget aggregate = new(cpu, mem);
                return nodeUsed.Cpu <= cpu && nodeUsed.Mem <= mem && nodeUsed != aggregate ? aggregate : null;
            }
        }
        return null;
    }

    [GeneratedRegex(@"-e\d+-(startup|step\d+x\d+|builtin)-", RegexOptions.CultureInvariant)]
    private static partial Regex StopStem();

    [Fact]
    public void RecordedBudgetFailuresMatchTheNode()
    {
        BudgetData fixture = Failures.Value;
        using Stream parameters = Golden.Open("g1/current-protocol-parameters.json");
        using JsonDocument g1 = JsonDocument.Parse(parameters);
        Assert.Equal(ReadModels(g1.RootElement), fixture.Models);
        Assert.Equal(568, fixture.Cases.Length);
        Assert.Equal([536, 8, 24], fixture.Cases.GroupBy(c => c.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count()));
        ExBudget?[] aggregates = [.. fixture.Cases.Select(PartialBuiltinAggregate)];
        Assert.Equal(99, aggregates.Count(a => a.HasValue));
        Dictionary<string, KnownFailure> known = ReadKnownFailures();
        CostModel[] models = [.. fixture.Models.Select((a, i) => CostModelLoader.FromFlat(a, i + 1, i == 2 ? Variant.E : Variant.D))];
        string?[] mismatches = new string?[fixture.Cases.Length];
        _ = Parallel.For(0, fixture.Cases.Length, i =>
        {
            BudgetCase entry = fixture.Cases[i];
            Exception? failure = Record.Exception(() => CheckReplay(entry, models[entry.Language - 1], aggregates[i], known.GetValueOrDefault(entry.Id)));
            if (failure is not null)
            {
                mismatches[i] = $"{entry.Id}: {failure.Message}";
            }
        });
        string[] errors = [.. mismatches.OfType<string>()];
        Assert.True(errors.Length == 0, $"{errors.Length} budget replay mismatches:\n{string.Join('\n', errors)}");
    }

    private static void CheckReplay(BudgetCase entry, CostModel model, ExBudget? aggregate, KnownFailure? known)
    {
        CekMachine machine = new CekMachine(entry.Budget, model).EnableDebugCounters();
        bool ok = false;
        CekValue? result = null;
        string stage = "decode";
        Exception? error = Record.Exception(() =>
        {
            Term<DeBruijn> decoded = entry.Decode();
            stage = "apply";
            Term<DeBruijn> applied = entry.Apply(decoded);
            stage = "evaluate";
            ok = machine.TryRunValue(applied, out result);
        });
        if (known is not null)
        {
            Assert.NotNull(error);
            Assert.True(known.Matches(stage, error), $"Changed {known.Owner} {stage} failure: {error.GetType().FullName}: {error.Message}");
            return;
        }
        if (entry.Outcome == "error" && stage == "evaluate")
        {
            Assert.Equal(BudgetStop.None, machine.StoppedAt);
            EvaluationException evaluation = Assert.IsType<EvaluationException>(error);
            string cause = entry.EvaluationError!.Split("Caused by: ", StringSplitOptions.None)[^1].Split(' ')[0];
            if (cause == "error")
            {
                Assert.Equal("ExplicitErrorTerm", evaluation.Message);
            }
            else
            {
                DefaultFunction function = Enum.Parse<DefaultFunction>(cause, ignoreCase: true);
                Assert.NotEqual("ExplicitErrorTerm", evaluation.Message);
                Assert.True(machine.DebugCounters.BuiltinCpu![(int)function] > 0);
            }
            return;
        }
        Assert.True(error is null, $"Unexpected {stage} failure: {error?.GetType().FullName}: {error?.Message}");
        if (entry.Outcome == "ok")
        {
            Assert.True(ok && !machine.RemainingBudget.IsExhausted);
            if (entry.Language == 3)
            {
                _ = Assert.IsType<UnitConstant>(Assert.IsType<VConstant>(result).Value);
            }
            return;
        }

        Assert.Equal("budget", entry.Outcome);
        Assert.False(ok);
        Assert.Null(result);
        Assert.Equal(aggregate is ExBudget total ? entry.Budget - total : entry.Remaining!.Value, machine.RemainingBudget);
        BudgetStop? expectedSite = (aggregate.HasValue, StopStem().Match(entry.Id).Groups[1].Value) switch
        {
            (true, _) or (_, "builtin") => BudgetStop.Builtin,
            (_, "startup") => BudgetStop.Startup,
            (_, "") => null,
            _ => BudgetStop.Steps
        };
        if (expectedSite.HasValue)
        {
            Assert.Equal(expectedSite.Value, machine.StoppedAt);
        }
        if (machine.StoppedAt == BudgetStop.Startup)
        {
            Assert.Equal(0, machine.DebugCounters.Steps!.Sum());
        }
        else if (machine.StoppedAt == BudgetStop.Steps && entry.Id.Contains("omega", StringComparison.Ordinal))
        {
            // Omega never reaches a final flush.
            Assert.Equal(0, machine.DebugCounters.Steps!.Sum() % MachineCosts.Slippage);
        }
    }

    [Fact]
    public void PeriodicStepStopDoesNotExecuteTheRestOfTheFastPath()
    {
        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        Term<DeBruijn> term = new ApplyTerm<DeBruijn>(new LambdaTerm<DeBruijn>(new(0), unit), unit);
        // 198 Force/Delay steps, then Apply (199) and the fast-path Lambda (200).
        for (int i = 0; i < (MachineCosts.Slippage - 2) / 2; i++)
        {
            term = new ForceTerm<DeBruijn>(new DelayTerm<DeBruijn>(term));
        }
        CekMachine machine = new CekMachine(new ExBudget(100, 100)).EnableDebugCounters();
        Assert.False(machine.TryRun(term, out _));
        Assert.Equal(BudgetStop.Steps, machine.StoppedAt);
        Assert.Equal(MachineCosts.Slippage, machine.DebugCounters.Steps!.Sum());
    }

    [Fact]
    public void StoppedMachineDoesNotRetainAbandonedCaseBranchesOrEnvironment()
    {
        CekMachine machine = new(new ExBudget(100, 100));
        WeakReference branch = StopWithAWaitingCase(machine);
        Assert.Equal(BudgetStop.Steps, machine.StoppedAt);
        Assert.Null(typeof(CekMachine).GetField("_env", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(machine));
        GC.Collect();
        Assert.False(branch.IsAlive);
        GC.KeepAlive(machine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StopWithAWaitingCase(CekMachine machine)
    {
        ConstTerm<DeBruijn> branch = new(UnitConstant.Instance);
        WeakReference reference = new(branch);
        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        Term<DeBruijn> scrutinee = unit;
        for (int i = 0; i < MachineCosts.Slippage / 2; i++)
        {
            scrutinee = new ForceTerm<DeBruijn>(new DelayTerm<DeBruijn>(scrutinee));
        }
        CaseTerm<DeBruijn> body = new(scrutinee, [branch]);
        Assert.False(machine.TryRunValue(new ApplyTerm<DeBruijn>(new LambdaTerm<DeBruijn>(new(0), body), unit), out _));
        return reference;
    }

    [Fact]
    public void BudgetEqualToTheCostPasses_OneUnitLessFails()
    {
        BudgetData fixture = Failures.Value;
        long[] costs = fixture.Models[2];
        byte[][] cbor = [[0xD8, 0x79, 0x80]];
        PlutusData[] arguments = [new PlutusDataConstr(0, [])];
        BudgetCase[] exact = [.. fixture.Cases.Where(c => c.Language == 3 && c.Outcome == "ok" && c.Id.EndsWith("-exact", StringComparison.Ordinal))];
        Assert.Equal(20, exact.Length);
        foreach (BudgetCase entry in exact)
        {
            ExUnitsResult expected = new((ulong)entry.Budget.Mem, (ulong)entry.Budget.Cpu);
            Assert.Equal(expected, Evaluator.EvaluateScript(entry.Flat, cbor, 3, 11, costs, budget: entry.Budget).ExUnits);
            Assert.Equal(expected, Evaluator.EvaluateScript(entry.Flat, arguments, 3, 11, costs, budget: entry.Budget).ExUnits);
        }
        foreach (BudgetCase entry in fixture.Cases.Where(c => c.Language == 3 && c.Outcome == "budget" &&
            (c.Id.EndsWith("-cpu-1", StringComparison.Ordinal) || c.Id.EndsWith("-mem-1", StringComparison.Ordinal) ||
             c.Id is "X-i2b-steps-minus1" or "X-i2b-mem-minus1")))
        {
            Reject(entry.Flat, entry.Budget);
        }

        BudgetCase i2b = FindBudgetCase("X-i2b-exact");
        Assert.Equal(new ExUnitsResult(2424, 1530707), Evaluator.EvaluateScript(i2b.Flat, cbor, 3, 11, costs).ExUnits);
        Assert.Equal(new ExUnitsResult(2424, 1530707), Evaluator.EvaluateScript(i2b.Flat, arguments, 3, 11, costs).ExUnits);
        Reject(i2b.Flat, new(long.MaxValue, 2423));
        Reject(i2b.Flat, new(1530706, long.MaxValue));

        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "conformance", "builtin", "semantics", "dropList", "dropList-15", "dropList-15.uplc"));
        Term<DeBruijn> drop = UplcParser.NameToDeBruijn(UplcParser.Parse(source)).Term;
        (Term<DeBruijn> result, ExUnitsResult units) = Evaluator.EvaluateTerm(drop);
        Assert.Equal("(con (list integer) [])", UplcParser.PrettyPrint(result));
        Assert.Equal(new ExUnitsResult(704, long.MaxValue), units);
        _ = Assert.Throws<BudgetExceededException>(() => Evaluator.EvaluateTerm(drop, ExBudget.Unlimited));
        CekMachine enforcing = new(ExBudget.Unlimited);
        Assert.False(enforcing.TryRun(drop, out _));
        Assert.Equal(BudgetStop.Builtin, enforcing.StoppedAt);

        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        foreach (ExBudget budget in new[] { new ExBudget(long.MinValue, long.MaxValue), new ExBudget(long.MaxValue, long.MinValue) })
        {
            CekMachine machine = new(budget);
            Assert.False(machine.TryRun(unit, out _));
            Assert.Equal(BudgetStop.Startup, machine.StoppedAt);
            Assert.Equal(budget - new ExBudget(100, 100), machine.RemainingBudget);
        }
        BudgetCase shortCpu = FindBudgetCase("X-i2b-steps-short-in-builtin");
        CekMachine failing = new(shortCpu.Budget, CostModelLoader.FromFlat(costs, 3, Variant.E));
        _ = Assert.Throws<BudgetExceededException>(() => failing.Run(shortCpu.Apply(shortCpu.Decode())));
        Assert.Equal(new ExBudget(-322707, 1300), failing.RemainingBudget);

        void Reject(byte[] flat, ExBudget budget)
        {
            _ = Assert.IsType<BudgetExceededException>(Assert.ThrowsAny<EvaluationException>(() => Evaluator.EvaluateScript(flat, cbor, 3, 11, costs, budget: budget)));
            _ = Assert.IsType<BudgetExceededException>(Assert.ThrowsAny<EvaluationException>(() => Evaluator.EvaluateScript(flat, arguments, 3, 11, costs, budget: budget)));
        }
    }

    [Fact]
    public void RestrictedRunsAllocateNothingExtra()
    {
        BudgetCase entry = FindBudgetCase("X-i2b-exact");
        CostModel model = CostModelLoader.FromFlat(Failures.Value.Models[2], 3, Variant.E);
        Term<DeBruijn> script = entry.Apply(entry.Decode());
        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        _ = new CekMachine(null, model).TryRun(script, out _);
        _ = new CekMachine(entry.Budget, model).TryRun(script, out _);
        _ = new CekMachine(new ExBudget(99, 100000)).TryRun(unit, out _);

        CekMachine measure = new(null, model);
        CekMachine restrict = new(entry.Budget, model);
        long start = GC.GetAllocatedBytesForCurrentThread();
        bool measured = measure.TryRun(script, out _);
        long measureBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        bool restricted = restrict.TryRun(script, out _);
        long restrictBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(measured && restricted);
        Assert.Equal(measureBytes, restrictBytes);

        CekMachine earlyStop = new(new ExBudget(99, 100000));
        start = GC.GetAllocatedBytesForCurrentThread();
        bool ran = earlyStop.TryRun(unit, out Term<DeBruijn>? result);
        long stoppedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.False(ran);
        Assert.Null(result);
        Assert.Equal(BudgetStop.Startup, earlyStop.StoppedAt);
        Assert.Equal(0, stoppedBytes);
    }

    [Fact]
    public void ReusedMachineChargesStartupPerRun()
    {
        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        foreach (ExBudget budget in new[] { new ExBudget(32200, 400), new ExBudget(32199, 400), new ExBudget(32200, 399), new ExBudget(32100, 300) })
        {
            CekMachine machine = new(budget);
            Assert.True(machine.TryRun(unit, out _));
            Assert.Equal(budget - new ExBudget(16100, 200), machine.RemainingBudget);
            bool exact = budget == new ExBudget(32200, 400);
            Assert.Equal(exact, machine.TryRun(unit, out _));
            Assert.Equal(exact ? BudgetStop.None : BudgetStop.Steps, machine.StoppedAt);
            Assert.Equal(budget - new ExBudget(32200, 400), machine.RemainingBudget);
        }
        BudgetCase entry = FindBudgetCase("B2-err-after-overspend");
        CekMachine afterError = new(entry.Budget, CostModelLoader.FromFlat(Failures.Value.Models[2], 3, Variant.E));
        _ = Assert.Throws<EvaluationException>(() => afterError.TryRun(entry.Apply(entry.Decode()), out _));
        Assert.Equal(BudgetStop.None, afterError.StoppedAt);
        Assert.Equal(new ExBudget(19900, 99900), afterError.RemainingBudget);
        Assert.Equal(new ExBudget(19900, 99900), afterError.RemainingBudget);
        Assert.True(afterError.TryRun(unit, out _));
        Assert.Equal(new ExBudget(3800, 99700), afterError.RemainingBudget);
    }
}
