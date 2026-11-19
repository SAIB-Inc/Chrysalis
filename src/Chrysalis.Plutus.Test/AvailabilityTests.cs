using System.Text.RegularExpressions;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Text;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class AvailabilityTests
{
    private static readonly PlutusData[] Args = [new PlutusDataConstr(0, [])];
    private static readonly (string Pin, Lazy<Oracle> Source)[] Oracles =
    [
        ("1.70.0.0", new(() => ReadOracle("1.70.0.0"))),
        ("1.71.0.0", new(() => ReadOracle("1.71.0.0")))
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BuiltinsFollowThePinnedVersions(int language)
    {
        long[] costs = NodeBudgetTests.Fixture.Models[language - 1];
        List<string> mismatches = [];
        for (int tag = 0; tag < 128; tag++)
        {
            // The delay is never forced: acceptance depends on decoding, not executing the builtin.
            Term<DeBruijn> body = new LambdaTerm<DeBruijn>(new(0), new ApplyTerm<DeBruijn>(
                new LambdaTerm<DeBruijn>(new(0), new ConstTerm<DeBruijn>(UnitConstant.Instance)),
                new DelayTerm<DeBruijn>(new BuiltinTerm<DeBruijn>((DefaultFunction)tag))));
            byte[] flat = FlatEncoder.EncodeProgram(new(new(1, 0, 0), body));
            for (int pv = 0; pv <= 11; pv++)
            {
                Exception? observed = Record.Exception(() => Evaluator.EvaluateScript(flat, Args, language, pv, costs));
                foreach ((string pin, Lazy<Oracle> source) in Oracles)
                {
                    Oracle oracle = source.Value;
                    Type? expected = pv >= oracle.Introduced[language] && pv >= oracle.Builtins[language].GetValueOrDefault(tag, int.MaxValue)
                        ? null : typeof(InvalidOperationException);
                    Compare(mismatches, $"{pin}/V{language}/PV{pv}/tag {tag}", expected, observed);
                }
            }
        }
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n{string.Join('\n', mismatches)}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ProgramVersionsAndCaseFollowThePinnedVersions(int language)
    {
        long[] costs = NodeBudgetTests.Fixture.Models[language - 1];
        byte[] v100 = Encode("1.0.0", "(con unit ())");
        byte[] v110 = Encode("1.1.0", "(case (constr 0) (con unit ()))");
        Program<DeBruijn> modern = Parse("1.1.0", "(case (constr 0) (con unit ()))");
        byte[] badCase = FlatEncoder.EncodeProgram(modern with { Version = new(1, 0, 0) });
        byte[] badConstr = FlatEncoder.EncodeProgram(Parse("1.1.0", "(constr 0)") with { Version = new(1, 0, 0) });
        List<string> mismatches = [];
        for (int pv = 0; pv <= 11; pv++)
        {
            foreach ((int version, byte[] flat) in new[] { (100, v100), (110, v110) })
            {
                Exception? observed = Record.Exception(() => Evaluator.EvaluateScript(flat, Args, language, pv, costs));
                foreach ((string pin, Lazy<Oracle> source) in Oracles)
                {
                    Oracle oracle = source.Value;
                    Type? expected = pv < oracle.Introduced[language] ? typeof(InvalidOperationException)
                        : pv < oracle.ProgramVersions[language][version] ? typeof(EvaluationException) : null;
                    Compare(mismatches, $"{pin}/V{language}/PLC {version}/PV{pv}", expected, observed);
                }
            }
            Check($"PLC 1.0.0 case/PV{pv}", typeof(InvalidOperationException), badCase, pv);
            Check($"PLC 1.0.0 constr/PV{pv}", typeof(InvalidOperationException), badConstr, pv);
        }
        Compare(mismatches, "raw case", typeof(InvalidOperationException), Record.Exception(() => FlatDecoder.DecodeProgram(badCase)));
        Compare(mismatches, "raw constr", typeof(InvalidOperationException), Record.Exception(() => FlatDecoder.DecodeProgram(badConstr)));
        if (language == 1)
        {
            Check("unit case", null, Encode("1.1.0", "(case (con unit ()) (con integer 42))"), 11);
            Check("Data case", typeof(EvaluationException), Encode("1.1.0", "(case (con data (Constr 0 [])) (con unit ()))"), 11);
        }
        if (language == 3)
        {
            byte[] boolCase = Encode("1.1.0", "(case (con bool True) (error) (con unit ()))");
            Check("boolean case before PV11", typeof(EvaluationException), boolCase, 10);
            Check("boolean case at PV11", null, boolCase, 11);
            Compare(mismatches, "raw boolean case", null, Record.Exception(() =>
                Evaluator.EvaluateTerm(new ApplyTerm<DeBruijn>(FlatDecoder.DecodeProgram(boolCase).Term,
                    new ConstTerm<DeBruijn>(new DataConstant(Args[0]))))));
        }
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n{string.Join('\n', mismatches)}");

        void Check(string scenario, Type? expected, byte[] flat, int pv) => Compare(mismatches, $"V{language}/{scenario}", expected,
            Record.Exception(() => Evaluator.EvaluateScript(flat, Args, language, pv, costs)));
    }

    [Fact]
    public void OversizedVariableIndexDoesNotWrapToABoundVariable()
    {
        byte[] bound = Convert.FromHexString("0101002320014981");
        byte[] unbound = Convert.FromHexString("010100232081808080104981"); // index 2^32 + 1
        _ = Evaluator.EvaluateScript(bound, Args, 3, 11);
        _ = Assert.Throws<UnboundVariableException>(() => Evaluator.EvaluateScript(unbound, Args, 3, 11));
    }

    [Theory]
    [InlineData("FEFFFFFF07", int.MaxValue - 1)]
    [InlineData("FFFFFFFF07", int.MaxValue)]
    [InlineData("8080808008", int.MaxValue)] // int.MaxValue + 1
    [InlineData("8180808010", int.MaxValue)] // 2^32 + 1
    public void FlatNaturalSaturatesOnlyBeyondInt32Max(string natural, int expected)
    {
        Program<DeBruijn> versioned = FlatDecoder.DecodeProgram(Convert.FromHexString($"01{natural}002499"));
        Assert.Equal(expected, versioned.Version.Minor);

        Program<DeBruijn> variable = FlatDecoder.DecodeProgram(Convert.FromHexString($"0101002320{natural}4981"));
        LambdaTerm<DeBruijn> outer = Assert.IsType<LambdaTerm<DeBruijn>>(variable.Term);
        ApplyTerm<DeBruijn> apply = Assert.IsType<ApplyTerm<DeBruijn>>(outer.Body);
        LambdaTerm<DeBruijn> inner = Assert.IsType<LambdaTerm<DeBruijn>>(apply.Function);
        Assert.Equal(expected, Assert.IsType<VarTerm<DeBruijn>>(inner.Body).Name.Index);
    }

    [Theory]
    [InlineData(1, 5, 9)]
    [InlineData(2, 7, 10)]
    [InlineData(3, 9, 9)]
    public void SemanticsVariantFollowsLanguageAndPv(int language, int introduced, int middle)
    {
        long[] costs = NodeBudgetTests.Fixture.Models[language - 1];
        string[] names = CostModelTests.DeployedNames(language);
        byte[] multiply = Encode("1.0.0", "[(lam _ (con unit ())) [(builtin multiplyInteger) (con integer 18446744073709551616) (con integer 340282366920938463463374607431768211456)]]");
        byte[] append = Encode("1.0.0", "[(lam _ (con unit ())) [(builtin appendString) (con string \"ééé\") (con string \"ééé\")]]");
        List<string> mismatches = [];
        Dictionary<char, (ExUnitsResult Multiply, ExUnitsResult Append)> units = [];
        for (int pv = introduced; pv <= 11; pv++)
        {
            // V1/V2/EvaluationContext.hs and V3/EvaluationContext.hs at the pinned tags.
            char variant = language == 3 ? (pv < 11 ? 'C' : 'E') : pv < 9 ? 'A' : pv < 11 ? 'B' : 'D';
            Exception? error = Record.Exception(() =>
            {
                (ExUnitsResult Multiply, ExUnitsResult Append) actual = (Run(multiply, pv), Run(append, pv));
                if (!units.TryAdd(variant, actual) && units[variant] != actual)
                {
                    mismatches.Add($"V{language}/PV{pv}: inconsistent variant {variant} budgets");
                }
            });
            Compare(mismatches, $"V{language}/PV{pv} costing", null, error);
        }
        if (units.TryGetValue('A', out (ExUnitsResult Multiply, ExUnitsResult Append) a) && units.TryGetValue('B', out (ExUnitsResult Multiply, ExUnitsResult Append) b))
        {
            CheckDelta("multiplyInteger A→B", a.Multiply, b.Multiply, Slope("multiplyInteger", "cpu"), 0);
        }
        if (units.TryGetValue(language == 3 ? 'C' : 'B', out (ExUnitsResult Multiply, ExUnitsResult Append) earlier) && units.TryGetValue(language == 3 ? 'E' : 'D', out (ExUnitsResult Multiply, ExUnitsResult Append) latest))
        {
            CheckDelta("appendString to PV11", latest.Append, earlier.Append, 4 * Slope("appendString", "cpu"), 4 * Slope("appendString", "memory"));
        }
        foreach (int pv in new[] { introduced, middle, 11 }.Distinct())
        {
            foreach (int n in new[] { -1, 256, 255 })
            {
                byte[] script = Encode("1.0.0", $"(force [(force (builtin ifThenElse)) [(builtin equalsByteString) [(builtin consByteString) (con integer {n}) (con bytestring #)] (con bytestring #{n & 255:x2})] (delay (con unit ())) (delay (error))])");
                Compare(mismatches, $"V{language}/PV{pv}/consByteString {n}", language == 3 && n != 255 ? typeof(EvaluationException) : null,
                    Record.Exception(() => Run(script, pv)));
            }
        }
        if (ExMem.StringExMem("aé𝄞", byBytes: false) != 3 || ExMem.StringExMem("aé𝄞", byBytes: true) != 1)
        {
            mismatches.Add("String sizing must count three code points or one UTF-8 word (ExMemoryUsage.hs:326–340).");
        }
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n{string.Join('\n', mismatches)}");

        ExUnitsResult Run(byte[] flat, int pv) => Evaluator.EvaluateScript(flat, Args, language, pv, costs).ExUnits;
        ulong Slope(string builtin, string dimension) => (ulong)costs[Array.IndexOf(names, $"{builtin}-{dimension}-arguments-slope")];
        void CheckDelta(string name, ExUnitsResult lower, ExUnitsResult higher, ulong cpu, ulong mem)
        {
            if (higher != new ExUnitsResult(lower.Mem + mem, lower.Steps + cpu))
            {
                mismatches.Add($"{name}: expected CPU +{cpu}, memory +{mem}; from {lower} to {higher}");
            }
        }
    }

    private static void Compare(List<string> mismatches, string scenario, Type? expected, Exception? observed)
    {
        if (observed?.GetType() != expected)
        {
            mismatches.Add($"{scenario}: expected {expected?.Name ?? "success"}, got {observed?.GetType().Name ?? "success"}: {observed?.Message}");
        }
    }

    private static Program<DeBruijn> Parse(string version, string body) => UplcParser.NameToDeBruijn(UplcParser.Parse($"(program {version} (lam ctx {body}))"));
    private static byte[] Encode(string version, string body) => FlatEncoder.EncodeProgram(Parse(version, body));

    private sealed record Oracle(Dictionary<int, int> Introduced, Dictionary<int, int>[] Builtins, Dictionary<int, int>[] ProgramVersions);

    private static Oracle ReadOracle(string pin)
    {
        string Read(string name)
        {
            using StreamReader reader = new(Golden.Open($"plutus-{pin}/Common/{name}.hs"));
            return Regex.Replace(reader.ReadToEnd(), @"(?s)\{-.*?-\}|--[^\r\n]*", "");
        }
        string versions = Read("Versions");
        Dictionary<string, int> pvs = Regex.Matches(Read("ProtocolVersions"), @"(?m)^(\w+PV)\s*=\s*MajorProtocolVersion\s+(\d+|maxBound)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value == "maxBound" ? int.MaxValue : int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        Dictionary<string, string[]> batches = [];
        foreach (Match m in Regex.Matches(versions, @"(?ms)^(batch\w+)\s*=\s*(.*?)(?=^\S)"))
        {
            string body = m.Groups[2].Value;
            batches.Add(m.Groups[1].Value, body.Contains('[', StringComparison.Ordinal) ? Names(body, @"[A-Z]\w*") : [.. Names(body, @"batch\w+").SelectMany(b => batches[b])]);
        }
        string Block(string name) => Regex.Match(versions, $@"(?ms)^{name}\s*=\s*\\case(.*?)(?=^\S)").Groups[1].Value;
        Dictionary<int, int> introduced = Regex.Matches(Block("ledgerLanguageIntroducedIn"), @"PlutusV(\d)\s*->\s*(\w+PV)")
            .ToDictionary(m => m.Groups[1].Value[0] - '0', m => pvs[m.Groups[2].Value]);
        Dictionary<string, int> tags = Enum.GetValues<DefaultFunction>().ToDictionary(f => f.ToString().Replace("_", "", StringComparison.Ordinal), f => (int)f, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(5, batches["batch7"].Length);
        Dictionary<int, int>[] builtins = Maps("builtinsIntroducedIn", @"\(\s*(\w+PV)\s*,\s*Set\.fromList\s*(\(.*?\)|\w+)\s*\)",
            (pv, body) => Names(body, @"batch\w+").SelectMany(b => batches[b]).Select(name =>
            {
                if (tags.TryGetValue(name.Replace("_", "", StringComparison.Ordinal), out int tag)) { return tag; }
                Assert.Equal(int.MaxValue, pv);
                Assert.Contains(name, batches["batch7"]);
                return -1;
            }).Where(tag => tag >= 0));
        Dictionary<int, int>[] plc = Maps("plcVersionsIntroducedIn", @"\(\s*(\w+PV)\s*,\s*Set\.fromList\s*\[(.*?)\]\s*\)",
            (_, body) => Names(body, @"(?<=plcVersion)\d+").Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
        return new(introduced, builtins, plc);

        Dictionary<int, int>[] Maps(string name, string pattern, Func<int, string, IEnumerable<int>> keys) =>
            [.. Regex.Split(Block(name), @"\bPlutusV\d\s*->").Select(block => Regex.Matches(block, pattern, RegexOptions.Singleline)
                .SelectMany(m => keys(pvs[m.Groups[1].Value], m.Groups[2].Value).Select(key => (Key: key, Pv: pvs[m.Groups[1].Value])))
                .ToDictionary(row => row.Key, row => row.Pv))];
        static string[] Names(string body, string pattern) => [.. Regex.Matches(body, pattern).Select(m => m.Value)];
    }
}
