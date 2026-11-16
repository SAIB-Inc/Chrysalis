using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline attribution contracts for each independently pinned CLI producer.</summary>
public sealed class B1GoldenProvenanceTests
{
    /// <summary>Every node's license and notice paths are Golden-relative and name retained bytes with verified digests.</summary>
    /// <param name="nodeVersion">The node associated with this producer pin.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("11.1.3")]
    [InlineData("11.2.1")]
    public void PinnedCliAttributionUsesExistingGoldenRelativePaths(string nodeVersion)
    {
        using JsonDocument sources = JsonDocument.Parse(Read("SOURCES.json"));
        JsonElement pin = sources.RootElement.GetProperty("cardano_cli_by_node").GetProperty(nodeVersion);
        Assert.Equal(pin.GetProperty("retained_license_sha256").GetString(), Hash(Read(pin.GetProperty("license_path").GetString()!)));
        Assert.Equal(pin.GetProperty("retained_notice_sha256").GetString(), Hash(Read(pin.GetProperty("notice_path").GetString()!)));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Read(string path)
    {
        Assert.False(Path.IsPathRooted(path));
        using Stream? resource = typeof(B1GoldenProvenanceTests).Assembly.GetManifestResourceStream("Golden/" + path);
        Assert.NotNull(resource);
        using MemoryStream bytes = new();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }
}
