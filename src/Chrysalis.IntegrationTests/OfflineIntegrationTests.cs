using System.Reflection;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline model and harness contracts, selected only by the integration category.</summary>
public sealed class OfflineIntegrationTests
{
    /// <summary>Checks that every integration fact has its category and known defects are explicit.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void EveryTestHasTheIntegrationCategory()
    {
        foreach (MethodInfo method in typeof(OfflineIntegrationTests).Assembly.GetTypes().SelectMany(type => type.GetMethods()).Where(method => method.GetCustomAttributes<FactAttribute>().Any()))
        {
            _ = Assert.Single(method.CustomAttributes, attribute => attribute.AttributeType == typeof(TraitAttribute)
                && (attribute.ConstructorArguments[0].Value as string) == "Category"
                && (attribute.ConstructorArguments[1].Value as string) == "Integration");
        }
    }

    /// <summary>Checks header stripping and value redaction using non-secret synthetic sentinels.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void DiagnosticsRemoveCredentialHeadersAndSentinels()
    {
        const string dummy = "synthetic-test-token-not-a-real-key";
        string clean = SafeDiagnostics.Redact("GET /\nproject_id: " + dummy + "\nerror " + dummy, dummy);
        Assert.DoesNotContain("project_id", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(dummy, clean, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", clean, StringComparison.Ordinal);
    }

    /// <summary>Checks independently recorded inner era/parameter/UTxO models after the two-byte result header.</summary>
    /// <param name="file">The original public capture name.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("c1-in-p7-003.cbor")]
    [InlineData("c1-in-p7-005.cbor")]
    [InlineData("c1-in-p7-007.cbor")]
    [InlineData("c1-in-p7-009.cbor")]
    [InlineData("c1-in-p7-011.cbor")]
    public void CapturedInnerModelsMatchIndependentFacts(string file)
    {
        byte[] raw = Resource("wire/" + file);
        Assert.Equal(0x82, raw[0]);
        Assert.Equal(4, raw[1]);
        ReadOnlyMemory<byte> payload = raw.AsMemory(2);
        switch (file)
        {
            case "c1-in-p7-003.cbor":
                Chrysalis.Network.Cbor.Common.Tip tip = CborSerializer.Deserialize<Chrysalis.Network.Cbor.Common.Tip>(payload);
                Chrysalis.Network.Cbor.Common.SpecificPoint point = Assert.IsType<Chrysalis.Network.Cbor.Common.SpecificPoint>(tip.Slot);
                Assert.Equal(139040643UL, point.Slot);
                Assert.Equal("6b39cb68f15c262776b550fb8ac375c2b76e13fad1dfe575d428c8e23c8df515", Convert.ToHexStringLower(point.Hash.Span));
                break;
            case "c1-in-p7-005.cbor":
                Assert.Equal(6UL, CborSerializer.Deserialize<CurrentEraQueryResponse>(payload).CurrentEra);
                break;
            case "c1-in-p7-007.cbor":
                using (JsonDocument cli = JsonDocument.Parse(Resource("current-protocol-parameters.json")))
                {
                    ProtocolOracles.AssertEquivalent(ProtocolOracles.Parameters(cli.RootElement), CborSerializer.Deserialize<CurrentProtocolParamsResponse>(payload).ProtocolParams);
                }
                break;
            case "c1-in-p7-009.cbor":
                using (JsonDocument expected = ProtocolOracles.GoldenUtxo())
                {
                    ProtocolOracles.AssertUtxos(expected.RootElement.GetProperty("utxos"), CborSerializer.Deserialize<UtxoByAddressResponse>(payload));
                }
                break;
            case "c1-in-p7-011.cbor":
                Assert.Empty(CborSerializer.Deserialize<UtxoByAddressResponse>(payload).Utxos);
                break;
            default:
                Assert.Fail("Unrecognized model fixture.");
                break;
        }
    }

    internal static byte[] Resource(string name)
    {
        using Stream stream = typeof(OfflineIntegrationTests).Assembly.GetManifestResourceStream("Preprod/" + name)
            ?? throw new InvalidOperationException("Missing public fixture: " + name);
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
