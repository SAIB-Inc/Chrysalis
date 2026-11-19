using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Independent real-node handshake captures required before the B1 repair.</summary>
public sealed class B1HandshakeCaptureTests
{
    /// <summary>The real wrong-magic reply retains the refusing Haskell node's text exactly.</summary>
    [Fact]
    public void WrongMagicCaptureRetainsTheNodesRefusal()
    {
        (HandshakeMessage message, JsonElement expected) = Capture("wrong-magic-node-11.1.3.cbor");
        Refuse reply = Assert.IsType<Refuse>(message);
        Refused reason = Assert.IsType<Refused>(reply.Reason);
        Assert.Equal(expected.GetProperty("version").GetInt32(), reason.VersionNumber);
        Assert.Equal(expected.GetProperty("message").GetString(), Assert.IsType<string>(reason.ErrorData));
    }

    /// <summary>A legacy Chrysalis proposal receives the real V23-only node's supported-version list.</summary>
    [Fact]
    public void LegacyClientCaptureRetainsV23OnlyMismatch()
    {
        (HandshakeMessage message, JsonElement expected) = Capture("legacy-mismatch-node-11.2.1.cbor");
        Refuse reply = Assert.IsType<Refuse>(message);
        VersionMismatch reason = Assert.IsType<VersionMismatch>(reply.Reason);
        Assert.Equal(expected.GetProperty("versions").EnumerateArray().Select(version => version.GetInt32()), reason.VersionNumbers);
    }

    /// <summary>The private Haskell node's V23 accept is decoded independently of the default offer.</summary>
    [Fact]
    public void PrivateDevnetCaptureAcceptsV23()
    {
        (HandshakeMessage message, JsonElement expected) = Capture("v23-accept-node-11.2.1.cbor");
        N2CAcceptVersion reply = Assert.IsType<N2CAcceptVersion>(message);
        Assert.Equal(expected.GetProperty("version").GetInt32(), reply.Version.Value);
        Assert.Equal(expected.GetProperty("networkMagic").GetUInt64(), reply.VersionData.NetworkMagic);
    }

    /// <summary>Portable command descriptions retain invocation structure without local executable/socket paths.</summary>
    /// <param name="name">The real capture whose source metadata is checked.</param>
    /// <param name="expectedCommand">The portable invocation with explicit executable and endpoint placeholders.</param>
    [Theory]
    [InlineData("wrong-magic-node-11.1.3.cbor", "<cardano-cli-10.13.1.0> latest query tip --testnet-magic 2 --socket-path <node-socket>")]
    [InlineData("legacy-mismatch-node-11.2.1.cbor", "dotnet <legacy-capture-client-7c45d47.dll> <node-socket> (references original Chrysalis 7c45d47 assemblies; magic 42)")]
    [InlineData("v23-accept-node-11.2.1.cbor", "docker exec <private-node-11.2.1> <cardano-cli-11.7.0.0> latest query tip --testnet-magic 42 --socket-path <node-socket>")]
    public void ManifestCommandsUsePortablePlaceholders(string name, string expectedCommand)
    {
        using JsonDocument manifest = JsonDocument.Parse(Read("manifest.json"));
        Assert.Equal(expectedCommand, manifest.RootElement.GetProperty("captures").GetProperty(name).GetProperty("command").GetString());
    }

    private static (HandshakeMessage Message, JsonElement Expected) Capture(string name)
    {
        using JsonDocument manifest = JsonDocument.Parse(Read("manifest.json"));
        JsonElement entry = manifest.RootElement.GetProperty("captures").GetProperty(name);
        byte[] bytes = Read(name);
        Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        HandshakeMessage message = CborSerializer.Deserialize<HandshakeMessage>(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(bytes, CborSerializer.Serialize(message));
        return (message, entry.GetProperty("expected").Clone());
    }

    private static byte[] Read(string name)
    {
        using Stream? stream = typeof(B1HandshakeCaptureTests).Assembly.GetManifestResourceStream("Chrysalis.Network.Tests.Fixtures.Handshake." + name);
        Assert.NotNull(stream);
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
