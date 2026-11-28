// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Text;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class ConformanceTests
{
    private const string DecodeMarker = "parse/decode error";
    private const string EvalMarker = "evaluation failure";

    private static readonly Pin NodePin = new("conformance.json");
    private static readonly Pin Batch7Pin = new("conformance-batch7.json");

    private enum OutcomeKind : byte { DecodeError, EvalFailure, Program }

    private enum WalkStatus : byte { Ok, Missing, Symlink }

    private sealed record CorpusPin(string TreeId, string TreePath, string[] BlobPaths, string Sha256, int Files, int Uplc, int Flat);

    // One pin file: its data, the digest of its fetched folder and its cost profile, each read at first use.
    private sealed class Pin
    {
        internal Pin(string file)
        {
            State = new(() => LoadPin(file));
            Corpus = new(() => ComputeCorpusDigest(this));
            Profile = new(() => LoadProfile(this));
        }

        internal Lazy<CorpusPin> State { get; }

        internal Lazy<(string Digest, int FileCount)> Corpus { get; }

        internal Lazy<CostModel> Profile { get; }
    }

    private static string CorpusRoot(Pin pin) => Path.Combine(AppContext.BaseDirectory, "plutus-conformance", pin.State.Value.TreeId);

    private static string SuiteRoot(Pin pin) => Path.Combine(CorpusRoot(pin), pin.State.Value.TreePath.Replace('/', Path.DirectorySeparatorChar));

    internal static string Input(string testCase, string form) => Input(NodePin, testCase, form);

    private static string Input(Pin pin, string testCase, string form)
    {
        ArgumentException.ThrowIfNullOrEmpty(testCase);
        ArgumentNullException.ThrowIfNull(form);
        if (form is not ("uplc" or "flat"))
        {
            throw new ArgumentOutOfRangeException(nameof(form), form, "Expected 'uplc' or 'flat'.");
        }

        EnsureVerified(pin);
        string caseDir = Path.Combine(SuiteRoot(pin), testCase.Replace('/', Path.DirectorySeparatorChar));
        return Path.Combine(caseDir, $"{Path.GetFileName(caseDir)}.{form}");
    }

    public static TheoryData<string, string> DiscoverTests() => Rows(NodePin);

    public static TheoryData<string, string> Batch7Cases() => Rows(Batch7Pin);

    private static TheoryData<string, string> Rows(Pin pin)
    {
        TheoryData<string, string> data = [];
        foreach ((string testCase, string form) in DiscoverRows(pin))
        {
            data.Add(testCase, form);
        }

        return data;
    }

    [Fact]
    public void TheCorpusIsTheNodePinnedSuite() => CheckCorpus(NodePin);

    [Fact]
    public void TheBatch7CasesAreTheNamedSelection()
    {
        CheckCorpus(Batch7Pin);
        Assert.Equal(2, Directory.GetFiles(AppContext.BaseDirectory, "conformance*.json").Length);
    }

    private static void CheckCorpus(Pin pin)
    {
        EnsureVerified(pin);
        CorpusPin data = pin.State.Value;

        List<(string Case, string Form)> rows = DiscoverRows(pin);
        Assert.Equal(data.Uplc, rows.Count(static r => r.Form == "uplc"));
        Assert.Equal(data.Flat, rows.Count(static r => r.Form == "flat"));
    }

    [Theory]
    [MemberData(nameof(DiscoverTests))]
    public void Conformance(string testCase, string form) => Check(NodePin, testCase, form);

    [Theory]
    [MemberData(nameof(Batch7Cases))]
    public void Batch7Conformance(string testCase, string form) => Check(Batch7Pin, testCase, form);

    private static void Check(Pin pin, string testCase, string form)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(form);

        string inputPath = Input(pin, testCase, form);
        string dir = Path.GetDirectoryName(inputPath)!;
        string leaf = Path.GetFileName(testCase);
        string expectedPath = $"{inputPath}.expected";
        string budgetPath = Path.Combine(dir, $"{leaf}.budget.expected");
        string textExpectedPath = Path.Combine(dir, $"{leaf}.uplc.expected");

        bool isFlat = form == "flat";
        string? expectedText = isFlat ? null : File.ReadAllText(expectedPath);
        byte[]? expectedBytes = isFlat ? File.ReadAllBytes(expectedPath) : null;
        string expectedBudget = File.ReadAllText(budgetPath).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        OutcomeKind expectedKind = ClassifyExpected(isFlat ? Encoding.UTF8.GetString(expectedBytes!) : expectedText!);

        (OutcomeKind actualKind, Program<DeBruijn>? actualInput, Term<DeBruijn>? actualResultTerm, string? actualBudget) =
            isFlat ? RunFlat(inputPath, pin) : RunText(inputPath, pin);

        List<string> failures = [];
        if (actualKind != expectedKind)
        {
            failures.Add(actualKind == OutcomeKind.DecodeError
                ? "decode"
                : expectedKind == OutcomeKind.DecodeError ? "accept" : "outcome");
        }
        else if (actualKind == OutcomeKind.DecodeError)
        {
            Assert.Equal(DecodeMarker, expectedBudget);
        }
        else if (actualKind == OutcomeKind.EvalFailure)
        {
            Assert.Equal(EvalMarker, expectedBudget);
        }
        else
        {
            Program<DeBruijn> expectedProg = ReadExpectedProgram(isFlat, expectedText, expectedBytes, textExpectedPath, failures);
            string actualPretty = UplcParser.PrettyPrint(new Program<DeBruijn>(actualInput!.Version, actualResultTerm!));
            if (!string.Equals(actualPretty, UplcParser.PrettyPrint(expectedProg), StringComparison.Ordinal))
            {
                failures.Add("result");
            }

            if (!string.Equals(actualBudget, expectedBudget, StringComparison.Ordinal))
            {
                failures.Add("budget");
            }
        }

        Assert.True(failures.Count == 0, $"{testCase} ({form}): fails with {string.Join('+', failures)}");
    }

    private static (OutcomeKind Kind, Program<DeBruijn>? Input, Term<DeBruijn>? Result, string? Budget) RunText(string path, Pin pin)
    {
        string source = File.ReadAllText(path);
        Program<DeBruijn> input;
        try
        {
            input = UplcParser.NameToDeBruijn(UplcParser.Parse(source));
        }
        catch (ParseException)
        {
            return (OutcomeKind.DecodeError, null, null, null);
        }
        catch (ConvertException)
        {
            return (OutcomeKind.DecodeError, null, null, null);
        }

        return EvaluateInput(input, pin);
    }

    private static (OutcomeKind Kind, Program<DeBruijn>? Input, Term<DeBruijn>? Result, string? Budget) RunFlat(string path, Pin pin)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Program<DeBruijn> input;
        try
        {
            input = FlatDecoder.DecodeProgram(bytes);
        }
        catch (InvalidOperationException)
        {
            return (OutcomeKind.DecodeError, null, null, null);
        }

        return Evaluator.IsClosed(input.Term, 0)
            ? EvaluateInput(input, pin)
            : (OutcomeKind.DecodeError, null, null, null);
    }

    private static (OutcomeKind Kind, Program<DeBruijn>? Input, Term<DeBruijn>? Result, string? Budget) EvaluateInput(Program<DeBruijn> input, Pin pin)
    {
        try
        {
            CekMachine machine = new(null, pin.Profile.Value, Availability.CaserFor(Availability.Dijkstra, input.Version));
            Term<DeBruijn> result = machine.Run(input.Term);
            ExUnitsResult units = Evaluator.UsedUnits(ExBudget.Unlimited, machine.RemainingBudget);
            string budget = string.Create(CultureInfo.InvariantCulture, $"({{cpu: {units.Steps}\n| mem: {units.Mem}}})");
            return (OutcomeKind.Program, input, result, budget);
        }
        catch (EvaluationException)
        {
            return (OutcomeKind.EvalFailure, null, null, null);
        }
    }

    private static OutcomeKind ClassifyExpected(string text) => text.Trim() switch
    {
        DecodeMarker => OutcomeKind.DecodeError,
        EvalMarker => OutcomeKind.EvalFailure,
        _ => OutcomeKind.Program
    };

    private static Program<DeBruijn> ReadExpectedProgram(
        bool isFlat,
        string? expectedText,
        byte[]? expectedBytes,
        string textExpectedPath,
        List<string> failures)
    {
        if (!isFlat)
        {
            return UplcParser.NameToDeBruijn(UplcParser.Parse(expectedText!));
        }

        try
        {
            return FlatDecoder.DecodeProgram(expectedBytes!);
        }
        catch (InvalidOperationException)
        {
            failures.Add("oracle");
            return UplcParser.NameToDeBruijn(UplcParser.Parse(File.ReadAllText(textExpectedPath)));
        }
    }

    private static List<(string Case, string Form)> DiscoverRows(Pin pin)
    {
        if (WalkTree(["plutus-conformance", pin.State.Value.TreeId, .. pin.State.Value.TreePath.Split('/')], out List<(string RelativePath, string FullPath)> entries, out _) != WalkStatus.Ok)
        {
            return [];
        }

        List<(string Case, string Form)> rows = [];
        foreach ((string rel, _) in entries)
        {
            string? form = Path.GetExtension(rel) switch
            {
                ".uplc" => "uplc",
                ".flat" => "flat",
                _ => null
            };
            if (form is null)
            {
                continue;
            }

            int slash = rel.LastIndexOf('/', StringComparison.Ordinal);
            string testCase = slash >= 0 ? rel[..slash] : ".";
            rows.Add((testCase, form));
        }

        rows.Sort(CompareRow);
        return rows;
    }

    private static int CompareRow((string Case, string Form) a, (string Case, string Form) b)
    {
        int cmp = string.CompareOrdinal(a.Case, b.Case);
        return cmp != 0 ? cmp : string.CompareOrdinal(a.Form, b.Form);
    }

    private static void EnsureVerified(Pin pin)
    {
        (string digest, int fileCount) = pin.Corpus.Value;
        Assert.Equal(pin.State.Value.Sha256, digest);
        Assert.Equal(pin.State.Value.Files, fileCount);
    }

    private static (string Digest, int FileCount) ComputeCorpusDigest(Pin pin)
    {
        WalkStatus status = WalkTree(["plutus-conformance", pin.State.Value.TreeId], out List<(string RelativePath, string FullPath)> entries, out string? refusedLink);
        if (status == WalkStatus.Missing)
        {
            Assert.Fail($"Missing Plutus conformance corpus at '{CorpusRoot(pin)}'; run src/Chrysalis.Plutus.Test/fetch_conformance.py");
        }

        if (status == WalkStatus.Symlink)
        {
            Assert.Fail($"Symlink refused in conformance corpus: {refusedLink}");
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        using IncrementalHash total = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string rel, string full) in entries)
        {
            using FileStream fs = File.OpenRead(full);
            string fileHash = Convert.ToHexStringLower(SHA256.HashData(fs));
            total.AppendData(Encoding.UTF8.GetBytes($"{fileHash}  {rel}\n"));
        }

        return (Convert.ToHexStringLower(total.GetHashAndReset()), entries.Count);
    }

    private static WalkStatus WalkTree(string[] segments, out List<(string RelativePath, string FullPath)> entries, out string? refusedLink)
    {
        entries = [];
        refusedLink = null;
        string root = AppContext.BaseDirectory;
        foreach (string segment in segments)
        {
            root = Path.Combine(root, segment);
            if (!TryGetAttributes(root, out FileAttributes rootAttrs))
            {
                return WalkStatus.Missing;
            }

            if (rootAttrs.HasFlag(FileAttributes.ReparsePoint))
            {
                refusedLink = root;
                return WalkStatus.Symlink;
            }

            if (!rootAttrs.HasFlag(FileAttributes.Directory))
            {
                return WalkStatus.Missing;
            }
        }

        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                FileAttributes attrs = File.GetAttributes(entry);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    entries.Clear();
                    refusedLink = entry;
                    return WalkStatus.Symlink;
                }

                if (attrs.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
                else
                {
                    string rel = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                    entries.Add((rel, entry));
                }
            }
        }

        return WalkStatus.Ok;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static CorpusPin LoadPin(string file)
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, file));
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;
        JsonElement tree = root.GetProperty("tree");
        string[] blobPaths = [.. root.GetProperty("blobs").EnumerateObject().Select(static p => p.Name)];
        return new(
            tree.GetProperty("id").GetString()!,
            tree.GetProperty("path").GetString()!,
            blobPaths,
            root.GetProperty("sha256").GetString()!,
            root.GetProperty("files").GetInt32(),
            root.GetProperty("uplc").GetInt32(),
            root.GetProperty("flat").GetInt32());
    }

    private static CostModel LoadProfile(Pin pin)
    {
        EnsureVerified(pin);
        Dictionary<string, long> costs = [];
        foreach (string blobPath in pin.State.Value.BlobPaths)
        {
            string fullPath = Path.Combine(CorpusRoot(pin), blobPath.Replace('/', Path.DirectorySeparatorChar));
            using FileStream stream = File.OpenRead(fullPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            Flatten(doc.RootElement, null, costs);
        }

        long[] values = [.. ParamNames.V3.Select(name => costs[name])];
        // plutus-conformance/CekEvaluator.hs:48-54: variant G, cost parameters E, and the caser of the newest protocol version per program (EvaluateInput).
        return CostModelLoader.FromFlat(values, 3, Variant.G);

        static void Flatten(JsonElement element, string? prefix, Dictionary<string, long> target)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    Flatten(prop.Value, prefix is null ? prop.Name : $"{prefix}-{prop.Name}", target);
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                target.Add(prefix!, element.GetInt64());
            }
        }
    }
}
