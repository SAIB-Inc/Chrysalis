using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Chrysalis.IntegrationTests;

internal static partial class SafeDiagnostics
{
    [GeneratedRegex(@"(?im)^\s*project_id\s*[:=][^\r\n]*(?:\r?\n|$)")]
    private static partial Regex CredentialHeader();

    internal static string StripHeaders(string text) => CredentialHeader().Replace(text, string.Empty);

    internal static string Redact(string text, params string[] secrets)
    {
        string clean = StripHeaders(text);
        foreach (string secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            clean = clean.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return clean;
    }
}

internal static class OracleCli
{
    private static string? _version;
    private static string? _executableSha256;
    private static string? _driverSha256;
    private static JsonElement _source;
    internal static async Task<string> QueryAsync(string label, CancellationToken cancellationToken, params string[] query)
    {
        string[] arguments = ["latest", "query", .. query,
            "--socket-path", IntegrationEnvironment.Required(IntegrationEnvironment.Socket),
            "--testnet-magic", IntegrationEnvironment.NetworkMagic.ToString(CultureInfo.InvariantCulture)];
        return await ExecuteAsync(label, arguments, cancellationToken);
    }

    internal static async Task<JsonElement> QueryJsonAsync(string label, CancellationToken cancellationToken, params string[] query)
    {
        string output = await QueryAsync(label, cancellationToken, query);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    internal static async Task<string> ExecuteAsync(string label, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool expectFailure = false)
    {
        string executable = IntegrationEnvironment.Required(IntegrationEnvironment.Cli);
        ProcessStartInfo start = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // Child CLI processes receive no credential-file configuration, let alone values.
        string[] allowed = ["PATH", "HOME", "TMP", "TEMP", "SystemRoot", "LD_LIBRARY_PATH"];
        start.Environment.Clear();
        foreach (string name in allowed)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start configured cardano-cli.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        string output = SafeDiagnostics.StripHeaders(await stdout);
        string error = SafeDiagnostics.StripHeaders(await stderr);
        if (arguments.Count == 1 && arguments[0] == "--version")
        {
            _version = output.Trim();
            _source = SourceForNode(IntegrationEnvironment.Required(IntegrationEnvironment.NodeVersion));
            string expectedVersion = "cardano-cli " + _source.GetProperty("version").GetString() + " ";
            Assert.StartsWith(expectedVersion, _version, StringComparison.Ordinal);
            string executableFile = File.Exists(executable) ? executable : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator).Select(directory => Path.Combine(directory, executable)).FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("Could not locate the executed cardano-cli driver for hashing.");
            (_driverSha256, _executableSha256) = FileIdentities(executableFile, Environment.GetEnvironmentVariable(IntegrationEnvironment.CliBinary));
        }
        string recorded = expectFailure || process.ExitCode != 0 ? output + error : output;
        Record(label, arguments, recorded);
        Assert.True(expectFailure ? process.ExitCode != 0 : process.ExitCode == 0, $"Unexpected cardano-cli exit code ({process.ExitCode}): {error}");
        return recorded.Trim();
    }

    internal static void Record(string label, IReadOnlyList<string> arguments, string publicOutput)
    {
        string directory = IntegrationEnvironment.OutputDirectory;
        string name = label + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".txt";
        string clean = SafeDiagnostics.StripHeaders(publicOutput);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(clean);
        File.WriteAllBytes(Path.Combine(directory, name), bytes);
        string entry = JsonSerializer.Serialize(new
        {
            file = name,
            source_repository = "https://github.com/IntersectMBO/cardano-cli",
            source_tag = _source.GetProperty("tag").GetString(),
            source_revision = _source.GetProperty("commit").GetString(),
            producer_source_path = _source.GetProperty("source_path").GetString(),
            cli_version = _version ?? throw new InvalidOperationException("Record the CLI identity before an oracle operation."),
            source_path = "generated cardano-cli output: " + string.Join(' ', arguments),
            cli_binary_sha256 = _executableSha256,
            cli_driver_sha256 = _driverSha256,
            node_source_repository = "https://github.com/IntersectMBO/cardano-node",
            node_tag = IntegrationEnvironment.Required(IntegrationEnvironment.NodeVersion),
            network_magic = IntegrationEnvironment.NetworkMagic,
            license = "Apache-2.0 (producer); public chain facts, no upstream implementation code copied",
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
        });
        File.AppendAllText(Path.Combine(directory, "golden-oracles.jsonl"), entry + Environment.NewLine);
    }

    internal static JsonElement SourceForNode(string nodeVersion)
    {
        using Stream stream = typeof(OracleCli).Assembly.GetManifestResourceStream("Golden/SOURCES.json")
            ?? throw new InvalidOperationException("Missing independent oracle producer manifest.");
        using JsonDocument sources = JsonDocument.Parse(stream);
        return sources.RootElement.GetProperty("cardano_cli_by_node").TryGetProperty(nodeVersion, out JsonElement source)
            ? source.Clone() : throw new InvalidOperationException($"No reviewed cardano-cli producer pin exists for node {nodeVersion}.");
    }

    internal static (string Driver, string Binary) FileIdentities(string driverPath, string? actualBinaryPath)
    {
        using FileStream driver = File.OpenRead(driverPath);
        string driverHash = Convert.ToHexStringLower(SHA256.HashData(driver));
        using FileStream binary = File.OpenRead(actualBinaryPath ?? driverPath);
        string binaryHash = Convert.ToHexStringLower(SHA256.HashData(binary));
        return (driverHash, binaryHash);
    }

    internal static async Task RecordVersionAsync(CancellationToken cancellationToken) =>
        _ = await ExecuteAsync("cli-version", ["--version"], cancellationToken);

    internal static async Task<T> AtStableTipAsync<T>(string label, Func<JsonElement, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            JsonElement before = await QueryJsonAsync(label + "-before", cancellationToken, "tip");
            T result = await operation(before, cancellationToken);
            JsonElement after = await QueryJsonAsync(label + "-after", cancellationToken, "tip");
            if (before.GetProperty("hash").GetString() == after.GetProperty("hash").GetString())
            {
                return result;
            }
        }
        throw new InvalidOperationException("The node tip moved during all three bounded oracle snapshots; no stable comparison was obtained.");
    }
}
