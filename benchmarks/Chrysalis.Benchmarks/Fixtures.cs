using System.Security.Cryptography;
using System.Text.Json;

namespace Chrysalis.Benchmarks;

internal static class Fixtures
{
    internal static string Root { get; } = LocateRoot();

    private static string LocateRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("CHRYSALIS_BENCHMARK_DATA");
        if (!string.IsNullOrEmpty(configured)) { return Path.GetFullPath(configured); }
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sha256.json"))) { return directory.FullName; }
            string sibling = Path.Combine(directory.FullName, "Chrysalis.Benchmarks");
            if (File.Exists(Path.Combine(sibling, "sha256.json"))) { return sibling; }
            string repository = Path.Combine(directory.FullName, "benchmarks", "Chrysalis.Benchmarks");
            if (File.Exists(Path.Combine(repository, "sha256.json"))) { return repository; }
        }
        throw new DirectoryNotFoundException("Set CHRYSALIS_BENCHMARK_DATA to the shared fixture project directory.");
    }

    internal static void Verify()
    {
        Dictionary<string, string> hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Root, "sha256.json"))) ?? throw new InvalidDataException("Missing SHA-256 manifest.");
        foreach ((string name, string expected) in hashes)
        {
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, name))));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Fixture SHA-256 mismatch: {name}");
            }
        }
        if (Directory.GetFiles(Path.Combine(Root, "fixtures"), "block-*.cbor").Length != 30
            || Directory.GetFiles(Path.Combine(Root, "fixtures"), "tx-*.cbor").Length != 164
            || Directory.GetFiles(Path.Combine(Root, "fixtures"), "txbody-*.cbor").Length != 164)
        {
            throw new InvalidDataException("The audit corpus must contain 30 blocks, 164 transactions and 164 bodies.");
        }
    }

    internal static byte[][] Load(string pattern) => [.. Directory.GetFiles(Path.Combine(Root, "fixtures"), pattern)
        .Order(StringComparer.Ordinal).Select(File.ReadAllBytes)];
}
