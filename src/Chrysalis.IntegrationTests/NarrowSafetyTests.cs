using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;
using Xunit;
using Xunit.Sdk;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline regressions for reviewed collateral, portability and complete UTxO expectations.</summary>
public sealed class NarrowSafetyTests
{
    /// <summary>Collateral identity, not an assumed ADA amount, determines which input is protected.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void CollateralOutrefIsExcludedRegardlessOfItsValue()
    {
        ResolvedInput collateral = Input(1, 6_000_000);
        ResolvedInput spend = Input(2, 7_000_000);
        FundingPlan plan = FundingSelection.CreatePlan([collateral, spend], collateral.Outref, WalletUtxos(collateral, spend));
        Assert.Same(spend, plan.Selected);
    }

    /// <summary>The test-only provider must never be given unselected inputs such as collateral.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void ProviderInventoryContainsOnlySelectedInputs()
    {
        ResolvedInput spend = Input(1, 7_000_000);
        ResolvedInput collateral = Input(2, 9_000_000);
        FundingPlan plan = FundingSelection.CreatePlan([spend, collateral], collateral.Outref, WalletUtxos(spend, collateral));
        Assert.Same(spend, Assert.Single(plan.ProviderInputs));
    }

    /// <summary>Any input added by a builder/provider must be detected before submission.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void TransactionBodyRejectsAnyUnselectedInput()
    {
        TransactionInput selected = Input(1, 7_000_000).Outref;
        TransactionInput collateral = Input(2, 9_000_000).Outref;
        FundingSelection.AssertExactlySelected([selected], [selected]);
        _ = Assert.ThrowsAny<XunitException>(() => FundingSelection.AssertExactlySelected([selected, collateral], [selected]));
        _ = Assert.ThrowsAny<XunitException>(() => FundingSelection.AssertExactlySelected([selected, selected], [selected]));
    }

    /// <summary>Credential location is supplied by the operator, without opening a credential in this test.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void AbsoluteCredentialPathIsPortableAndRelativePathIsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "synthetic-credential-location-never-opened.key");
        Assert.Equal(path, IntegrationEnvironment.ValidateCredentialPath(path, IntegrationEnvironment.BlockfrostFile));
        _ = Assert.Throws<InvalidOperationException>(() => IntegrationEnvironment.ValidateCredentialPath("relative.key", IntegrationEnvironment.BlockfrostFile));
    }

    /// <summary>A configured remote N2N endpoint must be a real discovery skip, not a same-node claim.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void NonLoopbackPeerIsSkippedWithReason()
    {
        string? original = Environment.GetEnvironmentVariable(IntegrationEnvironment.Host);
        try
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Host, "192.0.2.1");
            IntegrationFactAttribute fact = new([IntegrationEnvironment.Host]);
            Assert.NotNull(fact.Skip);
            Assert.Contains("loopback", fact.Skip, StringComparison.OrdinalIgnoreCase);
            _ = Assert.Throws<InvalidOperationException>(IntegrationEnvironment.RequireLoopbackHost);
            Assert.True(IntegrationEnvironment.IsLoopbackHost("localhost"));
            Assert.True(IntegrationEnvironment.IsLoopbackHost("127.0.0.1"));
            Assert.True(IntegrationEnvironment.IsLoopbackHost("::1"));
            Assert.False(IntegrationEnvironment.IsLoopbackHost("0.0.0.0"));
            Assert.False(IntegrationEnvironment.IsLoopbackHost("remote.example"));
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Host, "localhost");
            Assert.Equal("127.0.0.1", IntegrationEnvironment.RequireLoopbackHost());
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Host, original);
        }
    }

    /// <summary>The by-input query reference must be a real member of non-empty independent CLI UTxOs.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void InputReferenceComesFromTheNonemptyOracle()
    {
        using JsonDocument expected = ProtocolOracles.GoldenUtxo();
        JsonElement utxos = expected.RootElement.GetProperty("utxos");
        string reference = ProtocolOracles.SelectInputReference(utxos);
        Assert.True(utxos.TryGetProperty(reference, out _));
        TransactionInput selected = FundingSelection.ParseInputReference(reference);
        UtxoByAddressResponse captured = CborSerializer.Deserialize<UtxoByAddressResponse>(OfflineIntegrationTests.Resource("wire/c1-in-p7-009.cbor").AsMemory(2));
        FundingSelection.AssertExactlySelected([selected], captured.Utxos.Keys);
    }

    /// <summary>Full output comparison detects datum and reference-script changes, not only the outref.</summary>
    /// <param name="field">The independently absent output field to mutate.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("datum")]
    [InlineData("script")]
    public void CapturedOutputRejectsDatumAndScriptMutations(string field)
    {
        using JsonDocument expected = ProtocolOracles.GoldenUtxo();
        UtxoByAddressResponse original = CborSerializer.Deserialize<UtxoByAddressResponse>(OfflineIntegrationTests.Resource("wire/c1-in-p7-009.cbor").AsMemory(2));
        KeyValuePair<TransactionInput, ITransactionOutput> pair = Assert.Single(original.Utxos);
        IDatumOption? datum = field == "datum" ? DatumHashOption.Create(0, new byte[32]) : null;
        CborEncodedValue? script = field == "script" ? new CborEncodedValue(Convert.FromHexString("82024101")) : null;
        PostAlonzoTransactionOutput changed = PostAlonzoTransactionOutput.Create(new Address(pair.Value.Address().ToArray()), pair.Value.Amount(), datum, script);
        UtxoByAddressResponse actual = new(new Dictionary<TransactionInput, ITransactionOutput> { [pair.Key] = changed });
        _ = Assert.ThrowsAny<XunitException>(() => ProtocolOracles.AssertUtxos(expected.RootElement.GetProperty("utxos"), actual));
    }

    /// <summary>A legitimate 20 ADA spending input is not confused with a different collateral outref.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void TwentyAdaInputCanBeSelectedWhenItIsNotCollateral()
    {
        ResolvedInput spend = Input(1, 20_000_000);
        ResolvedInput collateral = Input(2, 6_000_000);
        FundingPlan plan = FundingSelection.CreatePlan([collateral, spend], collateral.Outref, WalletUtxos(collateral, spend));
        Assert.Same(spend, plan.Selected);
        string reference = Convert.ToHexStringLower(collateral.Outref.TransactionId.Span) + "#0";
        TransactionInput parsed = FundingSelection.ParseInputReference(reference);
        FundingSelection.AssertExactlySelected([parsed], [collateral.Outref]);
    }

    /// <summary>Missing public collateral configuration is a discovery skip with an actionable reason.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void MissingCollateralIsSkippedBeforeAnyCredentialRead()
    {
        string? original = Environment.GetEnvironmentVariable(IntegrationEnvironment.CollateralTxIn);
        try
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.CollateralTxIn, null);
            IntegrationFactAttribute fact = new([IntegrationEnvironment.CollateralTxIn]);
            Assert.NotNull(fact.Skip);
            Assert.Contains("public collateral txid#index", fact.Skip, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.CollateralTxIn, original);
        }
    }

    /// <summary>Invalid public collateral outrefs are rejected by pure parsing, before any wallet access.</summary>
    /// <param name="reference">The malformed public input reference.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("not-a-transaction#0")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000#-1")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000#0#1")]
    public void MalformedCollateralReferenceIsRejected(string reference) =>
        _ = Assert.Throws<InvalidOperationException>(() => FundingSelection.ParseInputReference(reference));

    /// <summary>Independent literal datum/script vectors compare successfully and detect byte mutations.</summary>
    /// <param name="form">The output field form to verify.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("hash")]
    [InlineData("inline")]
    [InlineData("script")]
    [InlineData("script-wrapped")]
    public void FullOutputComparesPresentDatumAndReferenceScript(string form)
    {
        using JsonDocument original = ProtocolOracles.GoldenUtxo();
        JsonObject expected = JsonNode.Parse(original.RootElement.GetProperty("utxos").GetRawText())!.AsObject();
        string outref = Assert.Single(expected).Key;
        JsonObject fields = expected[outref]!.AsObject();
        ResolvedInput source = Input(1, 10_000_000_000);
        IDatumOption? datum = null;
        CborEncodedValue? script = null;
        switch (form)
        {
            case "hash":
                fields["datumhash"] = new string('0', 64);
                datum = DatumHashOption.Create(0, new byte[32]);
                break;
            case "inline":
                _ = fields.Remove("datumhash");
                fields["inlineDatumhash"] = new string('0', 64);
                fields["inlineDatum"] = JsonNode.Parse("{\"int\":1}");
                fields["inlineDatumRaw"] = "01";
                datum = InlineDatumOption.Create(1, new CborEncodedValue(Convert.FromHexString("01")));
                break;
            case "script":
                fields["referenceScript"] = JsonNode.Parse("{\"scriptLanguage\":\"PlutusScriptLanguage PlutusScriptV2\",\"script\":{\"type\":\"PlutusScriptV2\",\"cborHex\":\"01\"}}");
                script = new CborEncodedValue(Convert.FromHexString("82024101"));
                break;
            case "script-wrapped":
                fields["referenceScript"] = JsonNode.Parse("{\"scriptLanguage\":\"PlutusScriptLanguage PlutusScriptV2\",\"script\":{\"type\":\"PlutusScriptV2\",\"cborHex\":\"424101\"}}");
                script = new CborEncodedValue(Convert.FromHexString("8202424101"));
                break;
            default:
                throw new InvalidOperationException("Unknown literal fixture form.");
        }
        PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(new Address(source.Output.Address().ToArray()), source.Output.Amount(), datum, script);
        UtxoByAddressResponse actual = new(new Dictionary<TransactionInput, ITransactionOutput> { [FundingSelection.ParseInputReference(outref)] = output });
        using (JsonDocument correct = JsonDocument.Parse(expected.ToJsonString()))
        {
            ProtocolOracles.AssertUtxos(correct.RootElement, actual);
        }
        if (form == "hash") { fields["datumhash"] = new string('1', 64); }
        if (form == "inline") { fields["inlineDatumRaw"] = "02"; }
        if (form is "script" or "script-wrapped") { fields["referenceScript"]!["script"]!["cborHex"] = "02"; }
        using JsonDocument changed = JsonDocument.Parse(expected.ToJsonString());
        _ = Assert.ThrowsAny<XunitException>(() => ProtocolOracles.AssertUtxos(changed.RootElement, actual));
    }

    /// <summary>A mistyped or stale configured outref cannot silently protect no wallet output.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void MissingConfiguredCollateralFailsBeforeSelection()
    {
        ResolvedInput spend = Input(1, 7_000_000);
        ResolvedInput missingCollateral = Input(2, 9_000_000);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            FundingSelection.CreatePlan([spend], missingCollateral.Outref, WalletUtxos(spend)));
        Assert.Contains("collateral", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unfiltered", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Collateral carrying tokens remains visible in the unfiltered map even when not a spending candidate.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void TokenBearingCollateralIsFoundInUnfilteredWalletMap()
    {
        ResolvedInput spend = Input(1, 7_000_000);
        ResolvedInput collateral = Input(2, 9_000_000);
        JsonObject unfiltered = JsonNode.Parse(WalletUtxos(spend, collateral).GetRawText())!.AsObject();
        JsonObject collateralValue = unfiltered[Reference(collateral.Outref)]!["value"]!.AsObject();
        collateralValue[new string('a', 56)] = new JsonObject { ["746f6b656e"] = 1 };
        JsonElement map = JsonSerializer.SerializeToElement(unfiltered);
        FundingPlan plan = FundingSelection.CreatePlan([spend], collateral.Outref, map);
        Assert.Same(spend, plan.Selected);
        Assert.Same(spend, Assert.Single(plan.ProviderInputs));
    }

    /// <summary>The Dijkstra cap measures input minus all returned outputs, not only a bounded fee.</summary>
    /// <param name="returned">Lovelace returned to the wallet.</param>
    /// <param name="allowed">Whether net spending stays within the cap.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(10_000_000UL, true)]
    [InlineData(59_800_000UL, true)]
    [InlineData(9_999_999UL, false)]
    [InlineData(0UL, false)]
    public void DijkstraNetSpendCountsAllUnreturnedValue(ulong returned, bool allowed)
    {
        void Check() => DijkstraProbes.AssertNetSpend(60_000_000, [Input(2, returned).Output]);
        if (allowed) { Check(); }
        else { _ = Assert.ThrowsAny<XunitException>(Check); }
    }

    private static JsonElement WalletUtxos(params ResolvedInput[] inputs) => JsonSerializer.SerializeToElement(
        inputs.ToDictionary(input => Reference(input.Outref), input => new { value = new { lovelace = input.Output.Amount().Lovelace() } }, StringComparer.Ordinal));

    private static string Reference(TransactionInput input) => Convert.ToHexStringLower(input.TransactionId.Span) + "#" + input.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ResolvedInput Input(byte id, ulong amount)
    {
        using JsonDocument expected = ProtocolOracles.GoldenUtxo();
        JsonProperty output = Assert.Single(expected.RootElement.GetProperty("utxos").EnumerateObject());
        byte[] address = Convert.FromHexString(output.Value.GetProperty("addressHex").GetString()!);
        return new ResolvedInput(TransactionInput.Create(Enumerable.Repeat(id, 32).ToArray(), 0),
            PostAlonzoTransactionOutput.Create(new Address(address), Lovelace.Create(amount)));
    }
}
