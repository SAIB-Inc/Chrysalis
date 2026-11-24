using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Providers.UtxoRpcTests;
using Chrysalis.Tx.Test.Cli;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class KupmiosTests
{
    internal static Kupmios Create(StubHttpHandler stub) => new("https://kupo.test/", "https://ogmios.test/", handler: stub);

    private static readonly Lazy<string> OgmiosParametersJson = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "providers", "ogmios", "protocol-parameters.json")));

    internal static string Match(string index = "\"output_index\":0,", string value = "\"coins\":15") =>
        $$"""{"transaction_id":"{{BlockfrostTests.Hash}}",{{index}}"address":"{{BlockfrostTests.Address}}","value":{ {{value}} } }""";

    internal static string OgmiosParameters(Action<JsonObject>? mutate = null)
    {
        JsonObject root = JsonNode.Parse(OgmiosParametersJson.Value)!.AsObject();
        JsonObject result = root["result"]!.AsObject();
        JsonObject models = [];
        foreach ((string name, JsonNode? model) in JsonNode.Parse(KoiosTests.Vectors.Value.Bytes(CliVectors.G1))!["costModels"]!.AsObject())
        {
            models["plutus:v" + name[^1]] = model!.DeepClone();
        }
        result["plutusCostModels"] = models;
        mutate?.Invoke(result);
        return root.ToJsonString();
    }

    private static void Set(JsonObject root, string dottedPath, JsonNode value)
    {
        string[] parts = dottedPath.Split('.');
        JsonObject current = root;
        for (int i = 0; i < parts.Length - 1; i++) { current = current[parts[i]]!.AsObject(); }
        current[parts[^1]] = value;
    }

    [Theory]
    [InlineData("\"output_index\":0,", "\"coins\":15,\"assets\":{\"00000000000000000000000000000000000000000000000000000000.a0\":-1}")]
    [InlineData("\"output_index\":0,", "")]
    [InlineData("", "\"coins\":15")]
    [InlineData("\"output_index\":0,", "\"coins\":15,\"assets\":{\"00000000000000000000000000000000000000000000000000000000.a0\":2,\"00000000000000000000000000000000000000000000000000000000.a0\":9}")]
    [InlineData("\"output_index\":0,", "\"coins\":15,\"assets\":{\"00000000000000000000000000000000000000000000000000000000.a0\":2,\"00000000000000000000000000000000000000000000000000000000.A0\":9}")]
    public async Task InvalidMatchNeverLooksLikeFunds(string index, string value)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("[" + Match(index, value) + "]"));
        using Kupmios provider = Create(stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        Assert.False(error.IsTransient);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KupoAbsenceIsEmptyArrayNeverNotFound(bool foundRoute)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("[]", foundRoute ? HttpStatusCode.OK : HttpStatusCode.NotFound));
        using Kupmios provider = Create(stub);
        if (foundRoute)
        {
            Assert.Empty(await provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
            Assert.Null(await provider.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 0, timeout.Token));
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<ProviderStatusException>(() =>
                provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<ProviderStatusException>(() =>
                provider.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 0, timeout.Token))).StatusCode);
        }
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task CancelsDuringMatchBodyRead()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using StubBody body = StubBody.Pending(entered);
        using StubHttpHandler stub = new(_ => new(HttpStatusCode.OK) { Content = new StreamContent(body) });
        using Kupmios provider = Create(stub);
        Task read = provider.GetUtxosAsync([BlockfrostTests.Address], caller.Token);
        await entered.Task.WaitAsync(timeout.Token);
        await caller.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
        Assert.True(body.WasDisposed);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchFaultDoesNotOverrideCallerCancellation(bool cancel)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using StubBody body = StubBody.Pending(entered);
        using StubHttpHandler stub = new(request => request.RequestUri!.AbsolutePath.EndsWith("/denied", StringComparison.Ordinal)
            ? StubHttpHandler.Json("forbidden", HttpStatusCode.Forbidden)
            : cancel ? new(HttpStatusCode.OK) { Content = new StreamContent(body) } : StubHttpHandler.Json("[]"));
        using Kupmios provider = Create(stub);
        Task read = provider.GetUtxosAsync(["denied", "other"], caller.Token);
        if (cancel)
        {
            await entered.Task.WaitAsync(timeout.Token);
            await caller.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Null(error.InnerException);
            Assert.True(body.WasDisposed);
        }
        else { Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ProviderStatusException>(() => read)).StatusCode); }
        Assert.Equal(2, stub.Requests.Count);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    [InlineData(400, true)]
    [InlineData(500, true)]
    public async Task OgmiosFailuresPreserveCodeOrStatusAndMaskDetail(int status, bool parameters)
    {
        const string key = "CANARY-ogmios-submit";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(status == 500 ? "not JSON"
            : $$"""{"jsonrpc":"2.0","error":{"code":3117,"message":"{{key}}"} }""", (HttpStatusCode)status));
        using Kupmios provider = new("https://kupo.test/", $"https://ogmios.test/{key}/", handler: stub);
        ProviderException error = await Assert.ThrowsAnyAsync<ProviderException>(async () =>
        {
            if (parameters) { _ = await provider.GetParametersAsync(timeout.Token); }
            else { _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token); }
        });
        if (status == 200) { Assert.IsType<ProviderException>(error); }
        else { Assert.Equal((HttpStatusCode)status, Assert.IsType<ProviderStatusException>(error).StatusCode); }
        if (status != 500)
        {
            Assert.Contains("3117", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("may have been accepted", error.Message, StringComparison.Ordinal);
            Assert.Equal(new string('*', key.Length), error.Detail);
        }
        Assert.Equal(status == 500, error.IsTransient);
        ProviderHttpTests.AssertNoCanary(error, key);
        Assert.Null(error.InnerException);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("""{"error":{"code":3117,"message":"\uD800"}}""", true)]
    [InlineData("""{"error":{"code":3117,"message":"broken"}""", false)]
    public async Task BadOgmiosDiagnosticKeepsHttpStatus(string body, bool hasCode)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ =>
        {
            HttpResponseMessage response = StubHttpHandler.Json(body, HttpStatusCode.BadRequest);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(4));
            return response;
        });
        using Kupmios provider = Create(stub);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() =>
            provider.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(4), error.RetryAfter);
        Assert.Equal(hasCode, error.Message.Contains("3117", StringComparison.Ordinal));
        if (hasCode) { Assert.Empty(error.Detail); }
        Assert.False(error.IsTransient);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("transaction_id")]
    [InlineData("address")]
    [InlineData("value")]
    [InlineData("null value")]
    [InlineData("negative coins")]
    [InlineData("negative index")]
    [InlineData("missing script language")]
    [InlineData("missing script bytes")]
    [InlineData("unknown script language")]
    [InlineData("missing datum hash")]
    [InlineData("null inline datum")]
    [InlineData("empty inline datum")]
    [InlineData("unresolved script")]
    [InlineData("script without hash")]
    [InlineData("wrong script hash")]
    [InlineData("short datum hash")]
    [InlineData("partial")]
    public async Task RequiredFieldsAndMalformedMatchesAreUnusable(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string match = Match();
        string payload = scenario switch
        {
            "transaction_id" => match.Replace($"\"transaction_id\":\"{BlockfrostTests.Hash}\",", "", StringComparison.Ordinal),
            "address" => match.Replace($"\"address\":\"{BlockfrostTests.Address}\",", "", StringComparison.Ordinal),
            "value" => match[..match.IndexOf(",\"value\"", StringComparison.Ordinal)] + "}",
            "null value" => match[..match.IndexOf(",\"value\"", StringComparison.Ordinal)] + ",\"value\":null}",
            "negative coins" => Match(value: "\"coins\":-1"),
            "negative index" => Match("\"output_index\":-1,"),
            "missing script language" => With(",\"script\":{\"script\":\"00\"}"),
            "missing script bytes" => With(",\"script\":{\"language\":\"plutus:v1\"}"),
            "unknown script language" => With($",\"script_hash\":\"{KoiosTests.PlutusV3Hash}\",\"script\":{{\"language\":\"plutus:v9\",\"script\":\"00\"}}"),
            "missing datum hash" => With(",\"datum_type\":\"hash\""),
            "null inline datum" => With($""", "datum_type":"inline", "datum_hash":"{BlockfrostTests.Hash}", "datum":null"""),
            "empty inline datum" => With(",\"datum_type\":\"inline\",\"datum\":\"\""),
            "unresolved script" => With($",\"script_hash\":\"{KoiosTests.PlutusV3Hash}\",\"script\":null"),
            "script without hash" => With(",\"script\":{\"language\":\"plutus:v3\",\"script\":\"450101002499\"}"),
            "wrong script hash" => With($",\"script_hash\":\"{KoiosTests.NativeHash}\",\"script\":{{\"language\":\"plutus:v3\",\"script\":\"450101002499\"}}"),
            "short datum hash" => With($",\"datum_type\":\"hash\",\"datum_hash\":\"{new string('a', 62)}\""),
            _ => match + ",{}"
        };
        string With(string extra) => match[..^1] + extra + "}";
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($"[{payload}]"));
        using Kupmios provider = Create(stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        Assert.False(error.IsTransient);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ExplicitZerosAreValidButOmittedDtoFieldsThrow()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($"[{Match(value: "\"coins\":0")}]"));
        using Kupmios provider = Create(stub);
        ResolvedInput output = Assert.Single(await provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        Assert.Equal(0UL, output.Outref.Index);
        Assert.Equal(0UL, output.Output.Amount().Lovelace());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KupoMatch>(Match("")));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KupoMatch>(Match(value: "")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergesIdenticalOverlapsAndRejectsConflicts(bool conflict)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(request => StubHttpHandler.Json(conflict && request.RequestUri!.AbsolutePath.Contains("pattern-b", StringComparison.Ordinal)
            ? $"[{Match(value: "\"coins\":16")}]" : $"[{Match()}]"));
        using Kupmios provider = Create(stub);
        if (conflict)
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetUtxosAsync(["pattern-a", "pattern-b"], timeout.Token));
            Assert.Contains("conflicting output", error.Message, StringComparison.Ordinal);
        }
        else { Assert.Single(await provider.GetUtxosAsync(["pattern-a", "pattern-b"], timeout.Token)); }
        Assert.Equal(2, stub.Requests.Count);
    }

    [Theory]
    [InlineData("CANARY-kupo-path")]
    [InlineData("CANARY-kupo-query")]
    [InlineData("CANARY-kupo-password")]
    [InlineData("CANARY-kupo-host")]
    [InlineData("CANARY-ogmios-path")]
    [InlineData("CANARY-ogmios-query")]
    [InlineData("CANARY-ogmios-password")]
    [InlineData("CANARY-ogmios-host")]
    [InlineData("%7E")]
    [InlineData("%51%4B%35")]
    [InlineData("%2F")]
    [InlineData("%71%37")]
    public async Task BothAdaptersMaskBothEndpoints(string token)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(request => StubHttpHandler.Json(token,
            request.Method == HttpMethod.Get ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest));
        using Kupmios provider = new("https://test:CANARY-kupo-password@CANARY-kupo-host.test/CANARY-kupo-path/?token=CANARY-kupo-query&one=%7E&three=%51%4B%35&slash=%2F",
            "https://%71%37:CANARY-ogmios-password@CANARY-ogmios-host.test/CANARY-ogmios-path/?token=CANARY-ogmios-query", handler: stub);
        ProviderStatusException kupo = await Assert.ThrowsAsync<ProviderStatusException>(() => provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        ProviderStatusException ogmios = await Assert.ThrowsAsync<ProviderStatusException>(() => provider.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
        ProviderHttpTests.AssertNoCanary(kupo, token);
        ProviderHttpTests.AssertNoCanary(ogmios, token);
        Assert.Equal(kupo.Detail, ogmios.Detail);
        Assert.Contains('*', kupo.Detail);
    }

    [Fact]
    public async Task ParametersAreTheNodesAnswer()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        (string Ogmios, string Cli, int Distinct)[] distinctThresholds =
        [
            ("stakePoolVotingThresholds.noConfidence", "poolVotingThresholds.motionNoConfidence", 501),
            ("stakePoolVotingThresholds.constitutionalCommittee.default", "poolVotingThresholds.committeeNormal", 502),
            ("stakePoolVotingThresholds.constitutionalCommittee.stateOfNoConfidence", "poolVotingThresholds.committeeNoConfidence", 503),
            ("stakePoolVotingThresholds.hardForkInitiation", "poolVotingThresholds.hardForkInitiation", 504),
            ("stakePoolVotingThresholds.protocolParametersUpdate.security", "poolVotingThresholds.ppSecurityGroup", 505),
            ("delegateRepresentativeVotingThresholds.noConfidence", "dRepVotingThresholds.motionNoConfidence", 506),
            ("delegateRepresentativeVotingThresholds.constitutionalCommittee.default", "dRepVotingThresholds.committeeNormal", 507),
            ("delegateRepresentativeVotingThresholds.constitutionalCommittee.stateOfNoConfidence", "dRepVotingThresholds.committeeNoConfidence", 508),
            ("delegateRepresentativeVotingThresholds.constitution", "dRepVotingThresholds.updateToConstitution", 509),
            ("delegateRepresentativeVotingThresholds.hardForkInitiation", "dRepVotingThresholds.hardForkInitiation", 510),
            ("delegateRepresentativeVotingThresholds.protocolParametersUpdate.network", "dRepVotingThresholds.ppNetworkGroup", 511),
            ("delegateRepresentativeVotingThresholds.protocolParametersUpdate.economic", "dRepVotingThresholds.ppEconomicGroup", 512),
            ("delegateRepresentativeVotingThresholds.protocolParametersUpdate.technical", "dRepVotingThresholds.ppTechnicalGroup", 513),
            ("delegateRepresentativeVotingThresholds.protocolParametersUpdate.governance", "dRepVotingThresholds.ppGovGroup", 514),
            ("delegateRepresentativeVotingThresholds.treasuryWithdrawals", "dRepVotingThresholds.treasuryWithdrawal", 515)
        ];
        (string Ogmios, string Cli, ulong Value)[] distinctIntegers =
        [
            ("stakePoolDeposit.ada.lovelace", "stakePoolDeposit", 500_000_001UL),
            ("delegateRepresentativeDeposit.ada.lovelace", "dRepDeposit", 500_000_002UL),
            ("constitutionalCommitteeMinSize", "committeeMinSize", 4UL),
            ("maxCollateralInputs", "maxCollateralInputs", 5UL)
        ];
        JsonObject distinctCli = JsonNode.Parse(KoiosTests.Vectors.Value.Bytes(CliVectors.G1))!.AsObject();
        string distinctOgmios = OgmiosParameters(result =>
        {
            foreach ((string ogmios, string cli, int n) in distinctThresholds)
            {
                Set(result, ogmios, JsonValue.Create(string.Create(CultureInfo.InvariantCulture, $"{n}/1000"))!);
                Set(distinctCli, cli, JsonValue.Create(n / 1000m)!);
            }
            foreach ((string ogmios, string cli, ulong v) in distinctIntegers)
            {
                Set(result, ogmios, JsonValue.Create(v)!);
                Set(distinctCli, cli, JsonValue.Create(v)!);
            }
        });
        int call = 0;
        string? seenBody = null;
        using StubHttpHandler stub = new(async (request, ct) =>
        {
            if (++call == 1)
            {
                seenBody = await request.Content!.ReadAsStringAsync(ct);
                return StubHttpHandler.Json(OgmiosParameters());
            }
            return StubHttpHandler.Json(distinctOgmios);
        });
        using Kupmios provider = Create(stub);

        using JsonDocument g1 = JsonDocument.Parse(KoiosTests.Vectors.Value.Bytes(CliVectors.G1));
        ProtocolParams actual = await provider.GetParametersAsync(timeout.Token);
        LedgerParameters.Equal(g1.RootElement, actual);
        Assert.Equal((HttpMethod.Post, "/"), Assert.Single(stub.Requests));
        using (JsonDocument sent = JsonDocument.Parse(seenBody!))
        {
            Assert.Equal("2.0", sent.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal("queryLedgerState/protocolParameters", sent.RootElement.GetProperty("method").GetString());
            Assert.False(sent.RootElement.TryGetProperty("params", out _));
        }

        using JsonDocument distinctExpected = JsonDocument.Parse(distinctCli.ToJsonString());
        LedgerParameters.Equal(distinctExpected.RootElement, await provider.GetParametersAsync(timeout.Token));
    }

    [Theory]
    [InlineData("no-result")]
    [InlineData("null")]
    [InlineData("no-version")]
    [InlineData("no-version-minor")]
    [InlineData("no-drep-lovelace")]
    [InlineData("no-max-value-bytes")]
    [InlineData("no-cost-models")]
    [InlineData("extra-plutus-v4")]
    [InlineData("no-drep-gov")]
    [InlineData("numeric-ratio")]
    [InlineData("negative-ref-base")]
    public async Task IncompleteParameterAnswersAreUnusable(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string body = scenario switch
        {
            "no-result" => "{\"jsonrpc\":\"2.0\",\"method\":\"queryLedgerState/protocolParameters\"}",
            "null" => "null",
            "no-version" => OgmiosParameters(r => _ = r.Remove("version")),
            "no-version-minor" => OgmiosParameters(r => _ = r["version"]!.AsObject().Remove("minor")),
            "no-drep-lovelace" => OgmiosParameters(r => _ = r["delegateRepresentativeDeposit"]!["ada"]!.AsObject().Remove("lovelace")),
            "no-max-value-bytes" => OgmiosParameters(r => _ = r["maxValueSize"]!.AsObject().Remove("bytes")),
            "no-cost-models" => OgmiosParameters(r => _ = r.Remove("plutusCostModels")),
            "extra-plutus-v4" => OgmiosParameters(r => r["plutusCostModels"]!["plutus:v4"] = new JsonArray(0)),
            "no-drep-gov" => OgmiosParameters(r => _ = r["delegateRepresentativeVotingThresholds"]!["protocolParametersUpdate"]!.AsObject().Remove("governance")),
            "numeric-ratio" => OgmiosParameters(r => r["monetaryExpansion"] = 0.003),
            _ => OgmiosParameters(r => r["minFeeReferenceScripts"]!["base"] = -1)
        };
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(body));
        using Kupmios provider = Create(stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token));
        Assert.False(error.IsTransient);
        Assert.StartsWith("Kupmios ", error.Message, StringComparison.Ordinal);
        Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.Single(stub.Requests);
    }

    private const string OgmiosStart = """{"jsonrpc":"2.0","method":"queryNetwork/startTime","result":"2026-01-01T00:00:00Z"}""";
    private const string OgmiosEnd = ""","end":{"time":{"seconds":1200},"slot":12000,"epoch":2}""";
    // An empty Byron era, then 0.1-second slots up to slot 12,000.
    private const string OgmiosEras = """{"jsonrpc":"2.0","method":"queryLedgerState/eraSummaries","result":[{"start":{"time":{"seconds":0},"slot":0,"epoch":0},"end":{"time":{"seconds":0},"slot":0,"epoch":0},"parameters":{"epochLength":4320,"slotLength":{"milliseconds":20000},"safeZone":864}},"""
        + """{"start":{"time":{"seconds":0},"slot":0,"epoch":0}""" + OgmiosEnd + ""","parameters":{"epochLength":6000,"slotLength":{"milliseconds":100},"safeZone":750}}]}""";

    // Answers Ogmios's start time, the given era summaries or the protocol parameters, and records each JSON-RPC method.
    private static StubHttpHandler SlotStub(string eras, ConcurrentQueue<string> methods) => new(async (request, ct) =>
    {
        using JsonDocument body = JsonDocument.Parse(await (request.Content ?? throw new InvalidOperationException("No request body.")).ReadAsStringAsync(ct));
        string method = body.RootElement.GetProperty("method").GetString() ?? "";
        methods.Enqueue(method);
        return StubHttpHandler.Json(method switch
        {
            "queryNetwork/startTime" => OgmiosStart,
            "queryLedgerState/eraSummaries" => eras,
            _ => OgmiosParameters()
        });
    });

    [Fact]
    public async Task TxBuilderRunsScriptsWithTheSlotProfileOgmiosReports()
    {
        ConcurrentQueue<string> methods = new();
        using StubHttpHandler stub = SlotStub(OgmiosEras, methods);
        using Kupmios provider = Create(stub);
        // The policy compares its redeemer with the context's range: slots 10 and 20 are 1 and 2 seconds after the start.
        _ = await new TxBuilder(provider).AddUnspentOutputs([SlotNetworkConfigTests.Funds]).ProvideCollateral([SlotNetworkConfigTests.Collateral])
            .SetChangeAddress(BlockfrostTests.Address).SetValidFrom(10).SetValidUntil(20)
            .AddMint(TestScripts.ValidityRange.HashHex(), new() { ["00"] = 1 }, TestScripts.ValidityRange, TestScripts.Range(1_767_225_601_000, 1_767_225_602_000))
            .Complete();
        Assert.Equal(["queryLedgerState/protocolParameters", "queryNetwork/startTime", "queryLedgerState/eraSummaries"], methods);
        _ = await new TxBuilder(provider).AddUnspentOutputs([SlotNetworkConfigTests.Funds]).PayLovelace(BlockfrostTests.Address, 2_000_000)
            .SetChangeAddress(BlockfrostTests.Address).Complete();
        Assert.Equal(4, methods.Count);
        Assert.Equal("queryLedgerState/protocolParameters", methods.Last());
    }

    [Theory]
    [InlineData("bounded", 12_000UL)]
    [InlineData("unbounded", null)]
    [InlineData("no-result", null)]
    public async Task OgmiosSlotProfileEndsWhereItsEraSummariesEnd(string scenario, ulong? end)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string eras = scenario switch
        {
            "bounded" => OgmiosEras,
            "unbounded" => OgmiosEras.Replace(OgmiosEnd, "", StringComparison.Ordinal),
            _ => "{\"jsonrpc\":\"2.0\",\"method\":\"queryLedgerState/eraSummaries\"}"
        };
        using StubHttpHandler stub = SlotStub(eras, new());
        using Kupmios provider = Create(stub);
        if (scenario == "no-result")
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetSlotNetworkConfigAsync(timeout.Token));
            Assert.False(error.IsTransient);
            Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
            return;
        }
        Assert.Equal(end, (await provider.GetSlotNetworkConfigAsync(timeout.Token)).EndSlot);
    }

    [Theory]
    [InlineData("0/1", 0UL, 1UL)]
    [InlineData("2/4", 2UL, 4UL)]
    [InlineData("18446744073709551615/18446744073709551615", ulong.MaxValue, ulong.MaxValue)]
    [InlineData("1/0", null, null)]
    [InlineData("-1/2", null, null)]
    [InlineData("+1/2", null, null)]
    [InlineData("1.5/2", null, null)]
    [InlineData(" 1/2", null, null)]
    [InlineData("1/2/3", null, null)]
    [InlineData("12", null, null)]
    [InlineData("18446744073709551616/1", null, null)]
    [InlineData(@"\u0031/2", null, null)]
    public async Task RatiosAreReadFromTheirUtf8Text(string raw, ulong? numerator, ulong? denominator)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string body = OgmiosParameters(r => r["monetaryExpansion"] = "__RATIO__").Replace("__RATIO__", raw, StringComparison.Ordinal);
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(body));
        using Kupmios provider = Create(stub);
        if (numerator is { } num && denominator is { } den)
        {
            CborRationalNumber rate = Assert.IsType<CborRationalNumber>((await provider.GetParametersAsync(timeout.Token)).ExpansionRate);
            Assert.Equal(num, rate.Numerator);
            Assert.Equal(den, rate.Denominator);
        }
        else
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token));
            Assert.False(error.IsTransient);
            Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
            Assert.Null(error.InnerException);
        }
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task QuantitiesUpToUInt64MaxMap()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string match = Match(value: "\"coins\":9223372036854775808,\"assets\":{\"00000000000000000000000000000000000000000000000000000000.a0\":18446744073709551615}");
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($"[{match}]"));
        using Kupmios provider = Create(stub);
        ResolvedInput output = Assert.Single(await provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        Assert.Equal(9_223_372_036_854_775_808UL, output.Output.Amount().Lovelace());
        KeyValuePair<ReadOnlyMemory<byte>, ulong> token = Assert.Single(Assert.Single(output.Output.Amount().MultiAsset()).Value.Value);
        Assert.Equal(ulong.MaxValue, token.Value);
    }

    [Fact]
    public async Task ScriptsAndDatumsKeepTheirHashes()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        const string nativeHex = "8200581cfa1133e7433ac9248a6f81da998b3f7445e203054961eba292bdb27d";
        string datumHash = new('a', 64);
        string[] matches =
        [
            Match("\"output_index\":0,")[..^1] + $",\"script_hash\":\"{KoiosTests.PlutusV3Hash}\",\"script\":{{\"language\":\"plutus:v3\",\"script\":\"450101002499\"}}}}",
            Match("\"output_index\":1,")[..^1] + $",\"script_hash\":\"{KoiosTests.NativeHash}\",\"script\":{{\"language\":\"native\",\"script\":\"{nativeHex}\"}}}}",
            Match("\"output_index\":2,")[..^1] + $",\"datum_type\":\"hash\",\"datum_hash\":\"{datumHash}\"}}",
            Match("\"output_index\":3,")[..^1] + $",\"datum_type\":\"inline\",\"datum_hash\":\"{BlockfrostTests.Hash}\",\"datum\":\"d87980\"}}"
        ];
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("[" + string.Join(",", matches) + "]"));
        using Kupmios provider = Create(stub);
        List<ResolvedInput> results = await provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token);
        Assert.Equal(4, results.Count);
        PlutusV3Script v3 = Assert.IsType<PlutusV3Script>(CborSerializer.Deserialize<IScript>(Assert.NotNull(results[0].Output.ScriptRef())));
        Assert.Equal(KoiosTests.PlutusV3Hash, v3.HashHex());
        MultiSigScript native = Assert.IsType<MultiSigScript>(CborSerializer.Deserialize<IScript>(Assert.NotNull(results[1].Output.ScriptRef())));
        Assert.Equal(KoiosTests.NativeHash, native.HashHex());
        DatumHashOption hashDatum = Assert.IsType<DatumHashOption>(Assert.IsType<PostAlonzoTransactionOutput>(results[2].Output).Datum);
        Assert.Equal(datumHash, Convert.ToHexStringLower(hashDatum.DatumHash.Span));
        Assert.Equal("d87980", Convert.ToHexStringLower(Assert.NotNull(results[3].Output.InlineDatumRaw()).Span));
    }
}
