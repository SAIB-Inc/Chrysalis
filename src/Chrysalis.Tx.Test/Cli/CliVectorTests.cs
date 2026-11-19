using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Xunit;

namespace Chrysalis.Tx.Test.Cli;

/// <summary>Replays the pinned offline CLI corpus and challenges its inventory.</summary>
public sealed class CliVectorTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "TestData", "cli");

    /// <summary>Each mutation fails closed without using the CLI or a node.</summary>
    /// <param name="damage">The change injected into a fresh copy of the corpus.</param>
    /// <param name="reason">A diagnostic that identifies the intended failure.</param>
    [Theory]
    [InlineData("byte", "cases/F01.tx")]
    [InlineData("absent", "cases/F01.tx")]
    [InlineData("payload-rehashed", "T1 file lines")]
    [InlineData("recipe-rehashed", "T1 file lines")]
    [InlineData("extra", "unlisted")]
    [InlineData("duplicate", "duplicate case")]
    [InlineData("kind", "unknown row kind")]
    [InlineData("empty", "no rows")]
    [InlineData("tier-row", "T1 case lines")]
    [InlineData("quote", "T1 case lines")]
    [InlineData("no-provenance", "provenance header changed")]
    [InlineData("no-case-module", "requires cases_T2.py with role control and since=T2")]
    public void DamagedCorpusIsRejected(string damage, string reason)
    {
        DirectoryInfo scratch = CopyCorpus();
        try
        {
            Damage(scratch.FullName, damage);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => CliVectors.Load(scratch.FullName));
            Assert.Contains(reason, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    /// <summary>Python bytecode left by tooling is not fixture payload.</summary>
    [Fact]
    public void StrayPythonBytecodeDoesNotInvalidateCorpus()
    {
        DirectoryInfo scratch = CopyCorpus();
        try
        {
            DirectoryInfo cache = scratch.CreateSubdirectory("__pycache__");
            File.WriteAllText(Path.Combine(cache.FullName, "capture.cpython-314.pyc"), "synthetic bytecode");
            _ = CliVectors.Load(scratch.FullName);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static DirectoryInfo CopyCorpus()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("cli-vectors-");
        foreach (string source in Directory.EnumerateFiles(FixtureRoot, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(scratch.FullName, Path.GetRelativePath(FixtureRoot, source));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest);
        }
        return scratch;
    }

    /// <summary>Every delivered row kind is present after checking the entire inventory.</summary>
    [Fact]
    public void VerifiedCorpusContainsAllKinds()
    {
        CliVectors vectors = CliVectors.Load(FixtureRoot);
        string[] kinds = [.. vectors.Cases.SelectMany(item => item.GetProperty("rows").EnumerateArray())
            .Select(row => CliVectors.Text(row, "kind")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Assert.Equal(["balanced", "candidate", "min-utxo", "quote"], kinds);
    }

    /// <summary>Every envelope decodes at Conway and each non-quote row satisfies its own relation.</summary>
    [Fact]
    public void TransactionsDecodeAndSatisfyTheirRelations()
    {
        CliVectors vectors = CliVectors.Load(FixtureRoot);
        Dictionary<string, ConwayTransactionBody> decoded = [];
        foreach ((string path, JsonElement file) in vectors.Files)
        {
            if (CliVectors.Text(file, "role") != "tx")
            {
                continue;
            }
            byte[] bytes = vectors.Bytes(path);
            PostMaryTransaction tx = Assert.IsType<PostMaryTransaction>(ITransaction.Read(bytes, Era.Conway, out int consumed));
            Assert.Equal(bytes.Length, consumed);
            Assert.Equal(bytes.Length, CborSerializer.GetEncodedLength(tx));
            decoded.Add(path, Assert.IsType<ConwayTransactionBody>(tx.Body));
            _ = tx.Witnesses;
        }

        foreach (JsonElement item in vectors.Cases)
        {
            foreach (JsonElement row in item.GetProperty("rows").EnumerateArray())
            {
                string path = CliVectors.Text(row, "tx");
                CheckRelation(vectors, row, decoded[path]);
            }
        }
    }

    private static void CheckRelation(CliVectors vectors, JsonElement row, ConwayTransactionBody body)
    {
        string kind = CliVectors.Text(row, "kind");
        if (kind == "quote")
        {
            return;
        }
        if (kind == "min-utxo")
        {
            ITransactionOutput output = body.Outputs.GetValue().ElementAt(checked((int)CliVectors.Number(row, "output")));
            Assert.Equal(CliVectors.Number(row, "minimum"), output.Amount().Lovelace());
            if (row.TryGetProperty("label", out JsonElement label))
            {
                PostAlonzoTransactionOutput tagged = Assert.IsType<PostAlonzoTransactionOutput>(output);
                CborEncodedValue data = CliVectors.Text(label, "layer") == "inline-datum"
                    ? Assert.IsType<InlineDatumOption>(tagged.Datum).Data
                    : Assert.IsType<CborEncodedValue>(tagged.ScriptRef);
                Assert.Equal(CliVectors.Number(label, "length"), (ulong)data.Value.Length);
            }
            return;
        }

        Dictionary<string, ulong> available = vectors.Lovelace(CliVectors.Text(row, "utxo"));
        ulong InputCoin(TransactionInput input) => available[$"{Convert.ToHexStringLower(input.TransactionId.Span)}#{input.Index}"];
        ulong withdrawals = body.Withdrawals is { } reward ? Sum(reward.Value.Values) : 0;
        ulong deposits = Sum(row.GetProperty("deposits").EnumerateObject().Select(item => item.Value.GetUInt64()));
        ulong outgoing = Sum(body.Outputs.GetValue().Select(output => output.Amount().Lovelace()));
        Assert.Equal(Sum(body.Inputs.GetValue().Select(InputCoin)) + withdrawals,
            outgoing + body.Fee + deposits + (body.Donation ?? 0));

        ulong q = CliVectors.Number(row, "q");
        string relation = kind == "candidate" ? CliVectors.Text(row, "relation") : "fee=quote";
        if (relation is "fee=quote" or "collateral<required")
        {
            Assert.Equal(q, body.Fee);
        }
        else
        {
            Assert.True(relation == "fee>quote" ? body.Fee > q : body.Fee < q);
        }

        if (body.Collateral is { } collateral)
        {
            ulong total = Assert.IsType<ulong>(body.TotalCollateral);
            ulong returned = Assert.IsAssignableFrom<ITransactionOutput>(body.CollateralReturn).Amount().Lovelace();
            Assert.Equal(Sum(collateral.GetValue().Select(InputCoin)), returned + total);
            ProtocolParams parameters = vectors.Parameters(CliVectors.Text(row, "parameters"));
            ulong percent = Assert.IsType<ulong>(parameters.CollateralPercentage);
            ulong minimum = checked(((body.Fee * percent) + 99) / 100);
            Assert.True(relation == "collateral<required" ? total < minimum : total >= minimum);
        }
        else
        {
            Assert.NotEqual("collateral<required", relation);
        }
    }

    private static ulong Sum(IEnumerable<ulong> values) => values.Aggregate(0UL, (total, value) => checked(total + value));

    /// <summary>Replay uses the captured wire result and overlays only the two recorded changes.</summary>
    [Fact]
    public void ParametersUseTheG1WireAndTheDerivedOverlay()
    {
        CliVectors vectors = CliVectors.Load(FixtureRoot);
        ProtocolParams baseline = vectors.Parameters(CliVectors.G1);
        JsonNode original = JsonNode.Parse(vectors.Bytes(CliVectors.G1))!;
        Assert.Equal(original["txFeePerByte"]!.GetValue<ulong>(), baseline.MinFeeA);
        Assert.Equal(original["txFeeFixed"]!.GetValue<ulong>(), baseline.MinFeeB);
        Assert.Equal(original["utxoCostPerByte"]!.GetValue<ulong>(), baseline.AdaPerUTxOByte);
        Assert.Equal(original["collateralPercentage"]!.GetValue<ulong>(), baseline.CollateralPercentage);
        CborRationalNumber baselinePrice = Assert.IsType<CborRationalNumber>(baseline.MinFeeRefScriptCostPerByte);
        Assert.Equal(original["minFeeRefScriptCostPerByte"]!.GetValue<decimal>(), (decimal)baselinePrice.Numerator / baselinePrice.Denominator);

        JsonNode modified = JsonNode.Parse(vectors.Bytes(CliVectors.Derived))!;
        ProtocolParams derived = vectors.Parameters(CliVectors.Derived);
        Assert.Equal(modified["utxoCostPerByte"]!.GetValue<ulong>(), derived.AdaPerUTxOByte);
        CborRationalNumber refPrice = Assert.IsType<CborRationalNumber>(derived.MinFeeRefScriptCostPerByte);
        Assert.Equal(modified["minFeeRefScriptCostPerByte"]!.GetValue<decimal>(), (decimal)refPrice.Numerator / refPrice.Denominator);
        foreach (string key in new[] { "utxoCostPerByte", "minFeeRefScriptCostPerByte" })
        {
            Assert.False(JsonNode.DeepEquals(original[key], modified[key]));
            modified[key] = original[key]!.DeepClone();
        }
        Assert.True(JsonNode.DeepEquals(original, modified));
    }

    private static void Damage(string root, string damage)
    {
        string tx = Path.Combine(root, "cases", "F01.tx");
        switch (damage)
        {
            case "byte":
                byte[] bytes = File.ReadAllBytes(tx);
                bytes[^1] ^= 1;
                File.WriteAllBytes(tx, bytes);
                return;
            case "absent":
                File.Delete(tx);
                return;
            case "payload-rehashed":
                RehashChangedFile(root, "cases/F01.tx");
                return;
            case "recipe-rehashed":
                RehashChangedFile(root, "cases.py");
                return;
            case "extra":
                File.WriteAllText(Path.Combine(root, "unlisted"), "unlisted control");
                return;
            default:
                break;
        }

        string path = Path.Combine(root, "manifest.json");
        List<string> lines = [.. File.ReadAllLines(path)];
        if (damage == "no-provenance")
        {
            lines.RemoveRange(1, lines.IndexOf("  \"files\": [") - 1);
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            return;
        }
        int index = lines.FindIndex(line => line.Contains("\"id\":\"F12b\"", StringComparison.Ordinal));
        if (damage == "no-case-module")
        {
            lines.Insert(index, """    {"id":"synthetic-quote","since":"T2","commands":[],"rows":[{"kind":"quote","tx":"cases/F01.tx","parameters":"inputs/g1/current-protocol-parameters.json","w":1,"s":0,"q":1}]},""");
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            return;
        }
        string original = lines[index];
        JsonNode @case = JsonNode.Parse(original.TrimEnd().TrimEnd(','))!;
        JsonArray rows = @case["rows"]!.AsArray();
        switch (damage)
        {
            case "duplicate":
                lines.Insert(index, original);
                break;
            case "kind":
                rows[0]!["kind"] = "unknown";
                break;
            case "empty":
                rows.Clear();
                break;
            case "tier-row":
                Assert.Equal(2, rows[0]!["s"]!.GetValue<int>());
                rows.RemoveAt(0);
                break;
            case "quote":
                rows[0]!["q"] = rows[0]!["q"]!.GetValue<ulong>() + 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(damage));
        }
        if (damage != "duplicate")
        {
            lines[index] = "    " + @case.ToJsonString() + ",";
        }
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }

    private static void RehashChangedFile(string root, string relativePath)
    {
        string target = Path.Combine(root, relativePath);
        File.AppendAllText(target, "\n");
        byte[] payload = File.ReadAllBytes(target);
        string path = Path.Combine(root, "manifest.json");
        string[] lines = File.ReadAllLines(path);
        int index = Array.FindIndex(lines, line => line.Contains($"\"path\":\"{relativePath}\"", StringComparison.Ordinal));
        string original = lines[index];
        JsonNode row = JsonNode.Parse(original.TrimEnd().TrimEnd(','))!;
        row["bytes"] = payload.Length;
        row["sha256"] = Convert.ToHexStringLower(SHA256.HashData(payload));
        lines[index] = "    " + row.ToJsonString() + (original.EndsWith(',', StringComparison.Ordinal) ? "," : "");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }
}
