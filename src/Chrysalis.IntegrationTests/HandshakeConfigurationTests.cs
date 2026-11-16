using System.Text.Json;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline contracts for the forced-version matrix and independent per-node CLI identity.</summary>
public sealed class HandshakeConfigurationTests
{
    /// <summary>A matrix iteration must send only the requested logical N2C version.</summary>
    /// <param name="logicalVersion">The requested logical version, not its wire number.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void ForcedOfferContainsOnlyTheRequestedVersion(int logicalVersion)
    {
        N2CVersionTable offer = IntegrationEnvironment.CreateN2COffer(42, logicalVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        KeyValuePair<N2CVersion, N2CVersionData> entry = Assert.Single(offer.Value);
        Assert.Equal(32768 + logicalVersion, entry.Key.Value);
        Assert.Equal(42UL, entry.Value.NetworkMagic);
        Assert.False(entry.Value.Query);
    }

    /// <summary>An absent force preserves the documented provisional default set.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void MissingForceUsesTheDefaultOffer() => Assert.Equal(8, IntegrationEnvironment.CreateN2COffer(1, null).Value.Count);

    /// <summary>A mistyped force is rejected offline, not silently changed to default negotiation.</summary>
    /// <param name="value">The invalid matrix configuration.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("15")]
    [InlineData("24")]
    [InlineData("32791")]
    [InlineData("garbage")]
    public void InvalidForceCannotSilentlyRunAnotherVersion(string value) =>
        _ = Assert.Throws<InvalidOperationException>(() => IntegrationEnvironment.CreateN2COffer(1, value));

    /// <summary>Each node pin must use the actually executable independent CLI producer.</summary>
    /// <param name="node">The node source tag.</param>
    /// <param name="version">The pinned CLI version for that node.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("11.1.3", "10.13.1.0")]
    [InlineData("11.2.1", "11.7.0.0")]
    public void EachNodeUsesItsOwnPinnedCli(string node, string version)
    {
        JsonElement source = OracleCli.SourceForNode(node);
        Assert.Equal(version, source.GetProperty("version").GetString());
    }

    /// <summary>An unreviewed node cannot inherit a producer pin that was never verified for it.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void UnknownNodeHasNoInventedCliPin() =>
        _ = Assert.Throws<InvalidOperationException>(() => OracleCli.SourceForNode("unreviewed-node"));

    /// <summary>A container driver is hashed separately from the public binary it actually executes.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void WrapperIdentityIsNotMislabeledAsCliBinary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chrysalis-b1-cli-identity-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            string driver = Path.Combine(directory, "synthetic-driver");
            string binary = Path.Combine(directory, "synthetic-cli-binary");
            File.WriteAllText(driver, "driver-public-marker\n");
            File.WriteAllText(binary, "actual-cli-binary-public-marker\n");
            (string driverHash, string binaryHash) = OracleCli.FileIdentities(driver, binary);
            Assert.NotEqual(driverHash, binaryHash);
            Assert.Equal("847c21c83878fe415fd5b6c9c2ce057cf127cb6fa07ca46bdced39461939cdb7", driverHash);
            Assert.Equal("b2d463d3b0605683fa125319f2d6bb2e251c8a422a4a7ed8c187d31dcc6fe6c5", binaryHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
