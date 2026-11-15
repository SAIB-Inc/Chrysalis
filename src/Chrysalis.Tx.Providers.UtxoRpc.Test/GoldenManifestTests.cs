using System.Security.Cryptography;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Verifies the provenance manifest and the exact bytes of every copied file.</summary>
public sealed class GoldenManifestTests
{
    /// <summary>No vector or spec file is missing, changed or unlisted.</summary>
    /// <returns>A task for reading the manifested files.</returns>
    [Fact]
    public async Task EveryCopiedFileMatchesTheManifest()
    {
        string root = AppContext.BaseDirectory;
        string[][] rows = [.. (await File.ReadAllLinesAsync(Path.Combine(root, "Golden", "manifest.tsv")))
            .Skip(1).Select(line => line.Split('\t'))];
        Assert.NotEmpty(rows);
        foreach (string[] row in rows)
        {
            Assert.Equal(8, row.Length);
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, row[0]));
            Assert.Equal(row[6], Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        string[] folders = ["Golden", "Protos/utxorpc"];
        string[] files = [.. folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(file) is not ("manifest.tsv" or "README.md" or ".gitattributes"))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))];
        Assert.Equal(files.Order(StringComparer.Ordinal), rows.Select(row => row[0]).Order(StringComparer.Ordinal));
    }
}
