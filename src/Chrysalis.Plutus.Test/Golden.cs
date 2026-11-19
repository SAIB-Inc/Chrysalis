using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Chrysalis.Plutus.Test;

internal static class Golden
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "CostModels");
    private static readonly Lazy<Dictionary<string, byte[]>> Files = new(Load);

    internal static Stream Open(string name) => new MemoryStream(Files.Value[name], writable: false);

    private static Dictionary<string, byte[]> Load()
    {
        using FileStream stream = File.OpenRead(Path.Combine(Root, "manifest.json"));
        using JsonDocument manifest = JsonDocument.Parse(stream);
        Dictionary<string, byte[]> files = [];
        foreach (JsonElement row in manifest.RootElement.EnumerateArray())
        {
            string path = row.GetProperty("path").GetString()!;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Root, path));
            Assert.Equal(row.GetProperty("bytes").GetInt32(), bytes.Length);
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            files.Add(path, bytes);
        }

        foreach (string fullPath in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            Assert.True(path is "manifest.json" or "fetch.py" or ".gitattributes" || files.ContainsKey(path), $"Unlisted golden file: {path}");
        }

        return files;
    }
}
