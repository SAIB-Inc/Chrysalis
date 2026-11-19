using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Providers;

namespace Chrysalis.Tx.Test.Cli;

internal sealed class CliVectors(string root, IReadOnlyDictionary<string, JsonElement> files, JsonElement[] cases)
{
    internal const string G1 = "inputs/g1/current-protocol-parameters.json";
    internal const string Wire = "inputs/g1/c1-in-p7-007.cbor";
    internal const string Derived = "inputs/protocol-parameters-derived.json";
    private const string T1Header = "8cc56e8d58a7c01bd24def151e9770c306b7c75667cc79522dcf39aeb8955be6";
    private const string T1FileLines = "c2a0f260f5c652100b036d2c2f9d8c6871c6fdc49da0d17e11ac1b975a862eb3";
    private const string T1CaseLines = "2b102d5eaa09bd7322629eeeac3715879273a8584b9af2a00c3589f43149f3b2";

    /// <summary>Root of the CLI fixtures copied to the test output.</summary>
    internal static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestData", "cli");
    internal IReadOnlyDictionary<string, JsonElement> Files { get; } = files;
    internal IReadOnlyList<JsonElement> Cases { get; } = cases;

    internal static CliVectors Load(string root)
    {
        try
        {
            string manifest = File.ReadAllText(Path.Combine(root, "manifest.json"));
            int filesStart = manifest.IndexOf("\n  \"files\": [\n", StringComparison.Ordinal);
            Require(filesStart >= 0 && Digest(manifest[..(filesStart + 1)]) == T1Header, "provenance header changed");
            using JsonDocument json = JsonDocument.Parse(manifest, new JsonDocumentOptions { AllowDuplicateProperties = false });
            Dictionary<string, JsonElement> files = [];
            JsonElement[] fileRows = [.. json.RootElement.GetProperty("files").EnumerateArray()];
            foreach (JsonElement file in fileRows)
            {
                string name = Text(file, "path");
                string role = Text(file, "role");
                Require(role is "parameters" or "parameters-wire" or "parameters-derived" or "cli-input"
                    or "attribution" or "control" or "tx" or "utxo", $"{name}: unknown file role {role}");
                Require(files.TryAdd(name, file.Clone()), $"duplicate file row: {name}");
                string path = Local(root, name);
                Require(File.Exists(path), $"missing manifested file: {name}");
                using FileStream data = File.OpenRead(path);
                Require((ulong)data.Length == Number(file, "bytes")
                    && Convert.ToHexStringLower(SHA256.HashData(data)) == Text(file, "sha256"), $"size or sha256 differs: {name}");
            }

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                bool pythonCache = name.EndsWith(".pyc", StringComparison.Ordinal)
                    && name.Split('/').SkipLast(1).Contains("__pycache__", StringComparer.Ordinal);
                Require(name is "manifest.json" or ".gitattributes" or ".gitignore" || pythonCache || files.ContainsKey(name), $"unlisted file: {name}");
            }

            Require(Role(files, G1) == "parameters" && Role(files, Wire) == "parameters-wire", "G1 JSON or wire row is missing");
            foreach (JsonElement file in files.Values.Where(file => Text(file, "role") == "parameters-derived"))
            {
                Require(Text(file.GetProperty("derivedFrom"), "sha256") == Text(files[G1], "sha256"),
                    $"{Text(file, "path")}: derivedFrom does not match G1's sha256");
            }

            JsonElement[] cases = [.. json.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.Clone())];
            Require(cases.Length != 0, "no cases");
            HashSet<string> ids = [];
            foreach (JsonElement item in cases)
            {
                string id = Text(item, "id");
                Require(ids.Add(id), $"duplicate case: {id}");
                string since = Text(item, "since");
                if (since != "T1")
                {
                    string module = $"cases_{since}.py";
                    Require(files.TryGetValue(module, out JsonElement control)
                        && Text(control, "role") == "control" && Text(control, "since") == since,
                        $"{id}: requires {module} with role control and since={since}");
                }
                JsonElement rows = item.GetProperty("rows");
                Require(rows.GetArrayLength() != 0, $"{id}: no rows");
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    ValidateRow(files, id, row);
                }
            }
            CheckDigest(manifest, "files", fileRows, T1FileLines);
            CheckDigest(manifest, "cases", cases, T1CaseLines);
            return new CliVectors(root, files, cases);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid CLI fixture inventory: " + ex.Message, ex);
        }
    }

    private static void ValidateRow(IReadOnlyDictionary<string, JsonElement> files, string id, JsonElement row)
    {
        Require(Role(files, Text(row, "tx")) == "tx", $"{id}: unlisted transaction");
        Require(Role(files, Text(row, "parameters")) is "parameters" or "parameters-derived", $"{id}: unlisted parameters");
        string kind = Text(row, "kind");
        switch (kind)
        {
            case "min-utxo":
                _ = Number(row, "output");
                _ = Number(row, "startCoin");
                _ = Number(row, "minimum");
                Require(!row.TryGetProperty("q", out _) && !row.TryGetProperty("w", out _) && !row.TryGetProperty("s", out _),
                    $"{id}: min-utxo row carries fee fields");
                if (row.TryGetProperty("label", out JsonElement label))
                {
                    Require(Text(label, "layer") is "inline-datum" or "reference-script", $"{id}: unknown label layer");
                    _ = Number(label, "length");
                }
                break;
            case "balanced":
            case "candidate":
            case "quote":
                _ = Number(row, "q");
                _ = Number(row, "w");
                _ = Number(row, "s");
                if (kind is "balanced" or "candidate")
                {
                    Require(Role(files, Text(row, "utxo")) == "utxo", $"{id}: unlisted UTxO file");
                    foreach (JsonProperty item in row.GetProperty("deposits").EnumerateObject())
                    {
                        _ = item.Value.GetUInt64();
                    }
                }
                if (kind == "candidate")
                {
                    Require(Text(row, "relation") is "fee>quote" or "fee<quote" or "collateral<required", $"{id}: unknown candidate relation");
                }
                break;
            default:
                throw new InvalidDataException($"{id}: unknown row kind {kind}");
        }
    }

    private static void CheckDigest(string manifest, string section, JsonElement[] rows, string expected)
    {
        string marker = $"\n  \"{section}\": [\n";
        int start = manifest.IndexOf(marker, StringComparison.Ordinal);
        Require(start >= 0, $"missing manifest {section} lines");
        start += marker.Length;
        int end = manifest.IndexOf("\n  ]", start, StringComparison.Ordinal);
        Require(end >= start, $"missing end of manifest {section} lines");
        string[] lines = manifest[start..end].Split('\n');
        Require(lines.Length == rows.Length, $"expected one line per manifest row in {section}");
        StringBuilder pinned = new();
        for (int i = 0; i < lines.Length; i++)
        {
            Require(lines[i].AsSpan().Trim().TrimEnd(',').SequenceEqual(rows[i].GetRawText()), $"{section}: line does not match parsed row");
            if (Text(rows[i], "since") == "T1")
            {
                _ = pinned.Append(lines[i]).Append('\n');
            }
        }
        string kind = section == "files" ? "file" : "case";
        Require(Digest(pinned.ToString()) == expected, $"T1 {kind} lines changed");
    }

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal byte[] Bytes(string path)
    {
        Require(Files.ContainsKey(path), $"unlisted file: {path}");
        byte[] bytes = File.ReadAllBytes(Local(root, path));
        if (Role(Files, path) != "tx")
        {
            return bytes;
        }
        using JsonDocument tx = JsonDocument.Parse(bytes);
        return Convert.FromHexString(Text(tx.RootElement, "cborHex"));
    }

    internal ProtocolParams Parameters(string path)
    {
        Require(path == G1 || Role(Files, path) == "parameters-derived", $"unknown parameter file: {path}");
        byte[] response = Bytes(Wire);
        Require(response.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x82, 0x04]), "G1 parameters: expected local-state-query result");
        ProtocolParams values = CborSerializer.Deserialize<CurrentProtocolParamsResponse>(response.AsMemory(2)).ProtocolParams;
        if (path == G1)
        {
            return values;
        }
        foreach (JsonProperty field in Files[path].GetProperty("derivedFrom").GetProperty("changes").EnumerateObject())
        {
            values = field.Name switch
            {
                "utxoCostPerByte" => values with { AdaPerUTxOByte = field.Value.GetUInt64() },
                "minFeeRefScriptCostPerByte" => values with { MinFeeRefScriptCostPerByte = ProviderValues.ExactRational(field.Value.GetDecimal()) },
                _ => throw new InvalidDataException($"unknown derived parameter: {field.Name}")
            };
        }
        return values;
    }

    internal Dictionary<string, ulong> Lovelace(string utxoPath)
    {
        Require(Role(Files, utxoPath) == "utxo", $"unknown UTxO file: {utxoPath}");
        using JsonDocument utxos = JsonDocument.Parse(Bytes(utxoPath));
        return utxos.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => Number(property.Value.GetProperty("value"), "lovelace"), StringComparer.Ordinal);
    }

    internal static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() is { Length: > 0 } text
        ? text : throw new InvalidDataException($"empty {key}");

    internal static ulong Number(JsonElement value, string key) => value.GetProperty(key).GetUInt64();

    private static string? Role(IReadOnlyDictionary<string, JsonElement> files, string path) =>
        files.TryGetValue(path, out JsonElement file) ? Text(file, "role") : null;

    private static string Local(string root, string path)
    {
        Require(!path.Contains('\\', StringComparison.Ordinal) && !path.Contains(':', StringComparison.Ordinal) && path.Split('/').All(part => part is not ("" or "." or "..")),
            $"invalid relative fixture path: {path}");
        return Path.Combine(root, path);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidDataException(reason);
        }
    }
}
