using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class NodeBudgetTests
{
    internal sealed record Script(string Id, int Language, string Purpose, byte[] Flat, ExUnitsResult? Units, ExUnitsResult? Budget);

    internal sealed record NodeData(long[][] Models, Script[] Scripts);

    internal static NodeData Fixture => Data.Value;

    private static readonly int[] NegativeV3Indexes = [52, 117, 133, 144];
    private static readonly int[] MeasuredCounts = [69, 161, 514];
    private static readonly Lazy<NodeData> Data = new(Load);
    private sealed record KnownFailure(string Owner, string Stage, string ExceptionType, string MessageSha256, string Reason);

    private static Dictionary<string, KnownFailure> ReadKnownFailures()
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
        Script[] scripts = [.. doc.RootElement.GetProperty("scripts").EnumerateArray()
            .Where(e => e.GetProperty("outcome").ValueEquals("ok"))
            .Select(e => new Script(e.GetProperty("id").GetString()!, e.GetProperty("lang").GetString() switch
            {
                "V1" => 1,
                "V2" => 2,
                "V3" => 3,
                _ => throw new InvalidDataException("Unexpected fixture language")
            }, e.GetProperty("purpose").GetString()!, Convert.FromHexString(e.GetProperty("flat").GetString()!),
                Units(e, "exunits"), Units(e, "budget")))];
        return new(models, scripts);
    }

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
        CostModel[] models = [.. fixture.Models.Select((a, i) => CostModelLoader.FromFlat(a, i + 1))];
        string?[] mismatches = new string?[scripts.Length];
        _ = Parallel.For(0, scripts.Length, i =>
        {
            Script script = scripts[i];
            ExUnitsResult expected = (script.Units ?? script.Budget)!;
            _ = known.TryGetValue(script.Id, out KnownFailure? failure);
            string stage = "decode";
            try
            {
                Term<DeBruijn> decoded = FlatDecoder.DecodeProgram(script.Flat).Term;
                stage = "apply";
                Term<DeBruijn> applied = ApplyLedgerArgs(script, decoded);
                stage = "evaluate";
                ExUnitsResult actual = Evaluator.Run(applied, models[script.Language - 1], script.Language, ExBudget.Unlimited).ExUnits;
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
                string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ex.Message)));
                if (failure is null || stage != failure.Stage || type != failure.ExceptionType || digest != failure.MessageSha256)
                {
                    mismatches[i] = $"{script.Id} V{script.Language}: unexpected {stage} failure: {type}: {ex.Message}";
                }
            }
        });
        Assert.All(known.Keys, id => Assert.Contains(scripts, s => s.Id == id));
        string[] errors = [.. mismatches.OfType<string>()];
        Assert.True(errors.Length == 0, $"{errors.Length} node replay mismatches:\n{string.Join('\n', errors)}");

        string[] stringCases = [.. scripts.Where(s => s.Id.StartsWith("W-builtin_semantics_", StringComparison.Ordinal))
            .Where(s => s.Id.Contains("appendString", StringComparison.Ordinal) || s.Id.Contains("encodeUtf8", StringComparison.Ordinal)
                || s.Id.Contains("equalsString", StringComparison.Ordinal)).Select(s => s.Id)];
        Assert.Equal(4, stringCases.Length);
        Assert.All(stringCases, id => Assert.DoesNotContain(id, known.Keys));

        CheckWitness("EXP-e0-V1", new(1701, 1148094), "VS-valueData-1x1-V1", DefaultFunction.ValueData);
        CheckWitness("BI-expMod-mod-hugeexp-control-V2", new(1701, 1148094), "VAL-insertCoin-positive-empty-V2", DefaultFunction.InsertCoin);
        CheckWitness("EXP-e0-V3", new(1401, 1100094), "L-dropList-head-2000", DefaultFunction.DropList);

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
            (ExUnitsResult raised, _, long raisedMem) = WithCounters(term, CostModelLoader.FromFlat(changed, witness.Language), function);
            Assert.True(raisedMem > mem);
            Assert.Equal(used.Mem + (ulong)(raisedMem - mem), raised.Mem);
            Assert.Equal(used.Steps, raised.Steps);
        }
    }

    private static int[] NegativeIndexes(IReadOnlyList<long> values) => [.. Enumerable.Range(0, values.Count).Where(i => values[i] < 0)];

    private static string Show(ExUnitsResult units) => FormattableString.Invariant($"{units.Mem} / {units.Steps}");

    private static Term<DeBruijn> ApplyLedgerArgs(Script script, Term<DeBruijn> term)
    {
        // plutigo node_ground_truth_test.go:97–110: recorded scripts ignore their arguments.
        int nargs = script.Language == 3 ? 1 : script.Purpose == "spend" ? 3 : 2;
        ConstTerm<DeBruijn> arg = new(new DataConstant(new PlutusDataConstr(0, [])));
        for (int i = 0; i < nargs; i++)
        {
            term = new ApplyTerm<DeBruijn>(term, arg);
        }
        return term;
    }

    private static (ExUnitsResult Used, long Cpu, long Mem) WithCounters(Term<DeBruijn> term, CostModel model, DefaultFunction function)
    {
        CekMachine machine = new CekMachine(ExBudget.Unlimited, model).EnableDebugCounters();
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
}
