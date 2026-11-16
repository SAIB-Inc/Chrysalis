using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Portable capture provenance, exhaustive embedded-resource attribution and checkout-stable hashes.</summary>
public sealed class B1CarbonProvenanceTests
{
    /// <summary>Capture provenance and user-facing docs must not depend on an external orchestration directory.</summary>
    /// <param name="relativePath">The repository-relative provenance document.</param>
    [Theory]
    [InlineData("src/Chrysalis.Network.Tests/Fixtures/Handshake/manifest.json")]
    [InlineData("src/Chrysalis.Network.Tests/Fixtures/Handshake/README.md")]
    [InlineData("docs/MANIFEST.md")]
    [InlineData("src/Chrysalis.IntegrationTests/README.md")]
    public void ProvenanceDocumentsAreRepositoryNeutral(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
        // Boundaries exclude actual repository names and prose such as node/ledger.
        Assert.DoesNotMatch(@"(?<![\w.-])(?:build[/]evidence|work[/]|node[/](?!\w)|chrysalis[-]update|[/]home[/])|original local [A-Z]\d+ worktree", text);
    }

    /// <summary>All fourteen pinned upstream LICENSE, NOTICE and CDDL files are checked against their independent digests.</summary>
    [Fact]
    public void UpstreamSourceManifestVerifiesEveryRetainedFile()
    {
        using JsonDocument sources = JsonDocument.Parse(Read("upstream-sources.json"));
        Assert.Equal(14, sources.RootElement.GetArrayLength());
        HashSet<string> files = [];
        foreach (JsonElement entry in sources.RootElement.EnumerateArray())
        {
            string file = entry.GetProperty("file").GetString()!;
            Assert.True(files.Add(file));
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(Read(file))));
            Assert.Matches("^[0-9a-f]{40}$", entry.GetProperty("source_commit").GetString()!);
            Assert.StartsWith("https://github.com/IntersectMBO/", entry.GetProperty("source_repository").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>No embedded handshake fixture can evade either the capture manifest or the upstream manifest.</summary>
    [Fact]
    public void EveryEmbeddedHandshakeResourceIsAccountedFor()
    {
        using JsonDocument manifest = JsonDocument.Parse(Read("manifest.json"));
        using JsonDocument sources = JsonDocument.Parse(Read("upstream-sources.json"));
        HashSet<string> expected =
        [
            ResourceName("manifest.json"), ResourceName("upstream-sources.json"), ResourceName("README.md"), ResourceName(".gitattributes")
        ];
        foreach (JsonProperty capture in manifest.RootElement.GetProperty("captures").EnumerateObject())
        {
            Assert.True(expected.Add(ResourceName(capture.Name)));
        }
        foreach (JsonElement source in sources.RootElement.EnumerateArray())
        {
            Assert.True(expected.Add(ResourceName(source.GetProperty("file").GetString()!)));
        }
        string[] actual = [.. typeof(B1CarbonProvenanceTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("Chrysalis.Network.Tests.Fixtures.Handshake.", StringComparison.Ordinal))];
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    /// <summary>Fixture bytes and attribution must not be rewritten by checkout line-ending conversion.</summary>
    /// <param name="directory">The fixture directory whose rules are checked.</param>
    [Theory]
    [InlineData("src/Chrysalis.Network.Tests/Fixtures/Handshake")]
    [InlineData("src/Chrysalis.IntegrationTests/Golden")]
    public void FixtureDirectoriesProtectBinaryAndAttributionBytes(string directory)
    {
        string attributes = Path.Combine(RepositoryRoot(), directory, ".gitattributes");
        Assert.True(File.Exists(attributes), "Fixture directories need explicit checkout attributes.");
        string[] lines = File.ReadAllLines(attributes);
        Assert.Contains("*.cbor binary", lines);
        Assert.Contains("*LICENSE -text", lines);
        Assert.Contains("*NOTICE -text", lines);
        Assert.Contains("*.cddl -text", lines);
        Assert.Contains("*.json -text", lines);
    }

    private static byte[] Read(string file)
    {
        using Stream? resource = typeof(B1CarbonProvenanceTests).Assembly.GetManifestResourceStream(ResourceName(file));
        Assert.NotNull(resource);
        using MemoryStream bytes = new();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string ResourceName(string file) => "Chrysalis.Network.Tests.Fixtures.Handshake." + file.Replace('/', '.');

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Chrysalis.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new InvalidOperationException("Run provenance contracts from a repository build.");
    }
}
