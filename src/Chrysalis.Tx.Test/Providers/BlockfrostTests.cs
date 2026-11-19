using System.Net;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class BlockfrostTests
{
    internal static string Hash { get; } = new('0', 64);
    internal static string Address { get; } = Chrysalis.Wallet.Models.Addresses.Address.FromBytes([0x60, .. new byte[28]]).ToBech32();
    internal static ITransaction Transaction { get; } = new TransactionBuilder().Build();

    internal static string Output(int index) => Output(index, null);

    internal static string Output(int index, string? referenceScriptHash = null) => System.Text.Json.JsonSerializer.Serialize(new
    {
        tx_hash = Hash,
        tx_index = index,
        output_index = index,
        reference_script_hash = referenceScriptHash,
        address = Address,
        amount = new[] { new { unit = "lovelace", quantity = "15" } }
    });

    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.TooManyRequests)]
    public async Task FailedPageNeverGivesPartialData(bool second, HttpStatusCode status)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(request => second && request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal)
            ? StubHttpHandler.Json("[" + string.Join(",", Enumerable.Range(0, 100).Select(Output)) + "]")
            : StubHttpHandler.Json("failure", status));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => provider.GetUtxosAsync(Address, timeout.Token));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(second ? 2 : 1, stub.Requests.Count);
    }

    [Fact]
    public async Task ByOutrefForbiddenIsNotAbsent()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("failure", HttpStatusCode.Forbidden));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task SubmitDoesNotExposeTheProjectId()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        const string key = "CANARY-Blockfrost-submit-project";
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(key, HttpStatusCode.Forbidden));
        using Blockfrost provider = new(key, handler: stub);
        Exception? error = await Record.ExceptionAsync(() => provider.SubmitTransactionAsync(Transaction, timeout.Token));
        Assert.NotNull(error);
        Assert.DoesNotContain(key, error.Message, StringComparison.OrdinalIgnoreCase);
        ProviderStatusException failure = Assert.IsType<ProviderStatusException>(error);
        Assert.Equal(new string('*', key.Length), failure.Detail);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("1", "a10202")]
    [InlineData("0", "a10002")]
    [InlineData("1", "a1010200")]
    [InlineData("1", "a201020103")]
    [InlineData("1", "bf01020103ff")]
    public async Task BadLabelDoesNotGivePartialMetadata(string label, string metadata)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(
            $$"""[{"label":"0","metadata":"a10001"},{"label":"{{label}}","metadata":"{{metadata}}"}]"""));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetTransactionMetadataAsync(Hash, timeout.Token));
        Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task AcceptsIndefiniteLengthMetadataMap()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("""[{"label":"1","metadata":"bf0102ff"}]"""));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        Metadata metadata = Assert.IsType<Metadata>(await provider.GetTransactionMetadataAsync(Hash, timeout.Token));
        KeyValuePair<ulong, ITransactionMetadatum> entry = Assert.Single(metadata.Value);
        Assert.Equal(1UL, entry.Key);
        Assert.Equal(2UL, Assert.IsType<MetadatumIntUlong>(entry.Value).Value);
    }

    // Pinned Blockfrost openapi-v0.1.93.yaml:14545 (description), 14559 (message example).
    internal const string Absent = """{"status_code":404,"error":"Not Found","message":"The requested component has not been found."}""";

    [Theory]
    [InlineData("address", true, Absent)]
    [InlineData("outref", true, Absent)]
    [InlineData("metadata", true, Absent)]
    [InlineData("address", true, """{"status_code":404,"error":"Not Found","message":"Different prose."}""")]
    [InlineData("address", false, "<html>404")]
    [InlineData("metadata", false, """{"status_code":404,"error":"Gateway Error","message":"Component not found"}""")]
    [InlineData("outref", false, """{"status_code":403,"error":"Not Found","message":"Component not found"}""")]
    [InlineData("address", false, """{"status_code":404,"error":"Not Found"}""")]
    [InlineData("outref", false, """{"status_code":404,"error":"Not Found","message":null}""")]
    [InlineData("metadata", false, """{"status_code":404,"error":"Not Found","message":123}""")]
    [InlineData("parameters", false, Absent)]
    public async Task AbsenceRequiresEndpointAndJsonMarker(string kind, bool absent, string body)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(body, HttpStatusCode.NotFound));
        // The structural credential is the marker's field name. Use the raw body for absence, not Detail.
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/status_code/", handler: stub);
        if (absent)
        {
            switch (kind)
            {
                case "address": Assert.Empty(await provider.GetUtxosAsync(Address, timeout.Token)); break;
                case "outref": Assert.Null(await provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token)); break;
                default: Assert.Null(await provider.GetTransactionMetadataAsync(Hash, timeout.Token)); break;
            }
        }
        else
        {
            Task Invoke() => kind switch
            {
                "address" => provider.GetUtxosAsync(Address, timeout.Token),
                "outref" => provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token),
                "metadata" => provider.GetTransactionMetadataAsync(Hash, timeout.Token),
                _ => provider.GetParametersAsync(timeout.Token)
            };
            Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<ProviderStatusException>(Invoke)).StatusCode);
        }
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("address", 1500, "not JSON")]
    [InlineData("outref", 5000, "not JSON")]
    [InlineData("metadata", 1500, "not JSON")]
    [InlineData("metadata", 5000, "")]
    public async Task AbsenceRequiresACompleteValidBody(string kind, int length, string suffix)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(Absent.PadRight(length) + suffix, HttpStatusCode.NotFound));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        Task Invoke() => kind switch
        {
            "address" => provider.GetUtxosAsync(Address, timeout.Token),
            "outref" => provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token),
            _ => provider.GetTransactionMetadataAsync(Hash, timeout.Token)
        };
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(Invoke);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal(1024, error.Detail.Length);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(0, 1)] // oversized page, rejected before the script on its first output
    [InlineData(1, 2)] // repeated final output from page one
    [InlineData(2, 3)] // A,B,A
    public async Task PagingRejectsOversizedAndOverlappingPages(int scenario, int count)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        int seen = 0;
        using StubHttpHandler stub = new(_ =>
        {
            seen++;
            int start = seen is 1 or 3 ? 0 : scenario == 1 ? 99 : 100;
            string[] outputs = [.. Enumerable.Range(start, scenario == 0 ? 101 : 100).Select(Output)];
            if (scenario == 0) { outputs[0] = Output(start, "script"); }
            return StubHttpHandler.Json("[" + string.Join(",", outputs) + "]");
        });
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetUtxosAsync(Address, timeout.Token));
        Assert.False(error.IsTransient);
        Assert.Equal(count, stub.Requests.Count);
        Assert.All(stub.Requests, call => Assert.Contains("/utxos?", call.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsBothIndicesAndQueriesARepeatedAddressOnce()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($"[{Output(0)},{Output(1)}]"));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        List<ResolvedInput> results = await provider.GetUtxosAsync([Address, Address], timeout.Token);
        Assert.Equal(new ulong[] { 0, 1 }, results.Select(utxo => utxo.Outref.Index));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task FailedBatchStopsOtherAddressesFromPaging()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string fullPage = $"[{string.Join(',', Enumerable.Range(0, 100).Select(Output))}]";
        using StubHttpHandler stub = new(request => request.RequestUri!.AbsolutePath.Contains("/limited/", StringComparison.Ordinal)
            ? StubHttpHandler.Json("rate limited", HttpStatusCode.TooManyRequests)
            : StubHttpHandler.Json(request.RequestUri.Query.Contains("page=1", StringComparison.Ordinal) ? fullPage : "[]"));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() =>
            provider.GetUtxosAsync(["limited", Address], timeout.Token));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Single(stub.Requests);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageTwoCannotBecomeAbsenceAndWaitsOnCaller(bool cancel)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using StubHttpHandler stub = new(async (request, ct) =>
        {
            if (request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal))
            {
                return StubHttpHandler.Json("[" + string.Join(",", Enumerable.Range(0, 100).Select(Output)) + "]");
            }
            if (cancel)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            HttpResponseMessage response = StubHttpHandler.Json(Absent, HttpStatusCode.NotFound);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
            return response;
        });
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        Task read = provider.GetUtxosAsync(Address, cancellation.Token);
        if (cancel)
        {
            await entered.Task.WaitAsync(timeout.Token);
            await cancellation.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Null(error.InnerException);
        }
        else
        {
            ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => read);
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(2), error.RetryAfter);
            Assert.Equal(Absent, error.Detail);
        }
        Assert.Equal(2, stub.Requests.Count);
    }

    [Theory]
    [InlineData("missing lovelace")]
    [InlineData("negative index")]
    [InlineData("partial")]
    [InlineData("repeated unit")]
    [InlineData("repeated asset")]
    [InlineData("missing outputs")]
    [InlineData("missing output index")]
    [InlineData("unknown cost model")]
    public async Task RejectsUnusableMappings(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string json = scenario switch
        {
            "missing lovelace" => "[" + Output(0).Replace("{\"unit\":\"lovelace\",\"quantity\":\"15\"}", "", StringComparison.Ordinal) + "]",
            "negative index" => $"[{Output(-1)}]",
            "partial" => $"[{Output(0)},{{}}]",
            "repeated unit" => "[" + Output(0).Replace("}]", "},{\"unit\":\"lovelace\",\"quantity\":\"15\"}]", StringComparison.Ordinal) + "]",
            "repeated asset" => $"[{Output(0).Replace("}]", $$"""},{"unit":"{{new string('0', 56)}}a0","quantity":"2"},{"unit":"{{new string('0', 56)}}A0","quantity":"9"}]""", StringComparison.Ordinal)}]",
            "missing outputs" => "{}",
            "missing output index" => """{"outputs":[{}]}""",
            _ => """
                {"cost_models_raw":{"PlutusV4":[0]},"max_tx_ex_mem":"0","max_tx_ex_steps":"0",
                 "max_block_ex_mem":"0","max_block_ex_steps":"0","max_val_size":"0","coins_per_utxo_size":"0",
                 "min_fee_ref_script_cost_per_byte":15}
                """
        };
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(json));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        async Task Invoke()
        {
            switch (scenario)
            {
                case "missing outputs" or "missing output index": await provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token); break;
                case "unknown cost model": await provider.GetParametersAsync(timeout.Token); break;
                default: await provider.GetUtxosAsync(Address, timeout.Token); break;
            }
        }
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(Invoke);
        Assert.False(error.IsTransient);
        Assert.Null(error.InnerException);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task NoMatchingIndexAndEmptyMetadataAreAbsent()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(request => StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/metadata/cbor", StringComparison.Ordinal)
            ? "[]" : """{"outputs":[{"output_index":1}]}"""));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        Assert.Null(await provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token));
        Assert.Null(await provider.GetTransactionMetadataAsync(Hash, timeout.Token));
        Assert.Equal(2, stub.Requests.Count);
    }

    // Include the legacy required fields except when testing absent parameters themselves.
    private const string LegacyParameters = """
        "max_tx_ex_mem":"0","max_tx_ex_steps":"0","max_block_ex_mem":"0","max_block_ex_steps":"0",
        "max_val_size":"0","coins_per_utxo_size":"0"
        """;

    [Theory]
    [InlineData("pre-Conway")]
    [InlineData("missing Conway price")]
    [InlineData("null model array")]
    [InlineData("one model")]
    public async Task MissingParametersStayUnknown(string scenario)
    {
        string json = scenario switch
        {
            "pre-Conway" => """{"protocol_major_ver":8,"protocol_minor_ver":0}""",
            "missing Conway price" => $$"""{"protocol_major_ver":9,{{LegacyParameters}}}""",
            "null model array" => $$"""{"min_fee_ref_script_cost_per_byte":15,"cost_models_raw":{"PlutusV1":null},{{LegacyParameters}}}""",
            _ => $$"""{"min_fee_ref_script_cost_per_byte":15,"cost_models_raw":{"PlutusV1":[0]},{{LegacyParameters}}}"""
        };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(json));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        if (scenario is "missing Conway price" or "null model array")
        {
            Assert.False((await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token))).IsTransient);
            return;
        }
        ProtocolParams parameters = await provider.GetParametersAsync(timeout.Token);
        if (scenario == "pre-Conway")
        {
            Assert.Equal(8UL, parameters.ProtocolVersion?.Major);
            Assert.Null(parameters.MinFeeA);
            Assert.Null(parameters.PoolVotingThresholds);
            Assert.Null(parameters.DRepVotingThresholds);
            Assert.Null(parameters.MinCommitteeSize);
            Assert.Null(parameters.CommitteeTermLimit);
            Assert.Null(parameters.GovernanceActionValidityPeriod);
            Assert.Null(parameters.GovernanceActionDeposit);
            Assert.Null(parameters.DRepDeposit);
            Assert.Null(parameters.DRepInactivityPeriod);
            Assert.Null(parameters.CostModelsForScriptLanguage);
            Assert.Null(parameters.MinFeeRefScriptCostPerByte);
            return;
        }
        CostMdls models = Assert.IsType<CostMdls>(parameters.CostModelsForScriptLanguage);
        Assert.Equal(0L, Assert.Single(models.PlutusV1!));
        Assert.Null(models.PlutusV2);
        Assert.Null(models.PlutusV3);
    }

    // ryo v7.0.1 txs_hash_utxos_2.sql:20-50; db-sync Babbage.hs:51-56.
    [Theory]
    [InlineData("failed transaction")]
    [InlineData("valid transaction")]
    [InlineData("unknown spent status")]
    [InlineData("malformed spent hash")]
    public async Task CollateralEntriesAreNotOutputs(string scenario)
    {
        string output = Output(7)[..^1];
        string[] outputs = scenario switch
        {
            "failed transaction" => [output + ""","collateral":true}""", output + ""","collateral":false,"consumed_by_tx":null}"""],
            "valid transaction" => [output + ""","collateral":true}"""],
            "malformed spent hash" => [output + ""","consumed_by_tx":"x"}"""],
            _ => [output + "}"]
        };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($$"""{"outputs":[{{string.Join(",", outputs)}}]}"""));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        if (scenario == "malformed spent hash")
        {
            Assert.False((await Assert.ThrowsAsync<ProviderException>(() => provider.GetUtxoByOutRefAsync(Hash, 7, timeout.Token))).IsTransient);
            return;
        }
        ResolvedInput? actual = await provider.GetUtxoByOutRefAsync(Hash, 7, timeout.Token);
        if (scenario == "valid transaction") { Assert.Null(actual); }
        else { Assert.Equal(7UL, Assert.IsType<ResolvedInput>(actual).Outref.Index); }
    }

    [Theory]
    [InlineData("sig")]
    [InlineData("all")]
    [InlineData("any")]
    [InlineData("atLeast")]
    [InlineData("after")]
    [InlineData("before")]
    public async Task ScriptsAreMappedAndChecked(string kind)
    {
        byte[] key = new byte[28];
        string signature = $$"""{"type":"sig","keyHash":"{{Convert.ToHexStringLower(key)}}"}""";
        ScriptPubKey leaf = ScriptPubKey.Create(0, key);
        CborList<INativeScript> scripts = CborList.Create<INativeScript>([leaf]);
        (string Json, INativeScript Script) example = kind switch
        {
            "sig" => (signature, leaf),
            "all" => ($$"""{"type":"all","scripts":[{{signature}}]}""", ScriptAll.Create(1, scripts)),
            "any" => ($$"""{"type":"any","scripts":[{{signature}}]}""", ScriptAny.Create(2, scripts)),
            "atLeast" => ($$"""{"type":"atLeast","required":1,"scripts":[{{signature}}]}""", ScriptNOfK.Create(3, 1, scripts)),
            "after" => ("""{"type":"after","slot":42}""", InvalidBefore.Create(4, 42)),
            _ => ("""{"type":"before","slot":43}""", InvalidHereafter.Create(5, 43))
        };
        MultiSigScript expected = MultiSigScript.Create(0, example.Script);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = ScriptHandler("timelock", $$"""{"json":{{example.Json}}}""");
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        MultiSigScript actual = Assert.IsType<MultiSigScript>(await provider.GetScript(expected.HashHex(), timeout.Token));
        Assert.Equal(CborSerializer.Serialize(expected.NativeScript), CborSerializer.Serialize(actual.NativeScript));
    }

    [Theory]
    [InlineData("timelock", "null")]
    [InlineData("timelock", "7")]
    [InlineData("timelock", """{"type":"unknown"}""")]
    [InlineData("timelock", """{"type":"sig","keyHash":"000000000000000000000000000000000000000000000000000000"}""")]
    [InlineData("timelock", """{"type":"after","slot":42}""")]
    [InlineData("plutusV3", "00")]
    public async Task BadScriptIsUnusable(string language, string content)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = ScriptHandler(language, language == "timelock"
            ? $$"""{"json":{{content}}}""" : $$"""{"cbor":"{{content}}"}""");
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetScript(new string('f', 56), timeout.Token));
        Assert.False(error.IsTransient);
    }

    [Theory]
    [InlineData("""{"type":"atLeast","required":-1,"scripts":[]}""", typeof(FormatException))]
    [InlineData("""{"type":"atLeast","required":2147483648,"scripts":[]}""", typeof(OverflowException))]
    public void NativeScriptRejectsInvalidThreshold(string json, Type exceptionType)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Throws(exceptionType, () => ProviderValues.NativeScript(document.RootElement));
    }

    private static StubHttpHandler ScriptHandler(string language, string body) => new(request =>
        StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith(language == "timelock" ? "/json" : "/cbor", StringComparison.Ordinal)
            ? body : $$"""{"type":"{{language}}"}"""));

    [Fact]
    public async Task OutputIndexIsTheIndex()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string output = Output(7).Replace("\"tx_index\":7,", "", StringComparison.Ordinal);
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($"[{output}]"));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        Assert.Equal(7UL, Assert.Single(await provider.GetUtxosAsync(Address, timeout.Token)).Outref.Index);
    }

    [Theory]
    [InlineData("\"0\"", 0UL)]
    [InlineData("\"18446744073709551615\"", ulong.MaxValue)]
    [InlineData("5", 5UL)]
    [InlineData("\"18446744073709551616\"", null)]
    [InlineData("\"-1\"", null)]
    [InlineData("\"+1\"", null)]
    [InlineData("\"1.0\"", null)]
    [InlineData("\" 1\"", null)]
    [InlineData("\"\"", null)]
    public async Task NumbersParseFromUtf8(string quantity, ulong? expected)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string output = Output(0).Replace("\"15\"", quantity, StringComparison.Ordinal);
        using StubHttpHandler stub = new(request => StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/addresses/", StringComparison.Ordinal)
            ? $"[{output}]" : $$"""{"outputs":[{{output}}]}"""));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        async Task Check(Func<Task<ResolvedInput?>> read)
        {
            if (expected is null) { Assert.False((await Assert.ThrowsAsync<ProviderException>(read)).IsTransient); }
            else { Assert.Equal(expected.Value, Assert.IsType<ResolvedInput>(await read()).Output.Amount().Lovelace()); }
        }
        await Task.WhenAll(
            Check(async () => Assert.Single(await provider.GetUtxosAsync(Address, timeout.Token))),
            Check(() => provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token)));
        Assert.Equal(2, stub.Requests.Count);
    }

    [Theory]
    [InlineData("2000000", "15", 15UL, 1UL)]
    [InlineData("\"-1\"", "15", null, null)]
    [InlineData("\"2000000\"", "12.5", 25UL, 2UL)]
    public async Task ParameterNumbersParseFromUtf8(string deposit, string price, ulong? numerator, ulong? denominator)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json($$"""
            {"protocol_major_ver":9,"key_deposit":{{deposit}},"min_fee_ref_script_cost_per_byte":{{price}},{{LegacyParameters}}}
            """));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        if (numerator is null)
        {
            Assert.False((await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token))).IsTransient);
            return;
        }
        ProtocolParams parameters = await provider.GetParametersAsync(timeout.Token);
        Assert.Equal(2_000_000UL, parameters.KeyDeposit);
        Assert.Equal(numerator, parameters.MinFeeRefScriptCostPerByte?.Numerator);
        Assert.Equal(denominator, parameters.MinFeeRefScriptCostPerByte?.Denominator);
    }

    [Fact]
    public async Task CallerCanRetryReadsButLostSubmitIsSentOnlyOnce()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        int gets = 0;
        using StubHttpHandler stub = new(request => request.Method == HttpMethod.Post ? throw new IOException("CANARY-lost-answer")
            : ++gets == 1 ? StubHttpHandler.Json("busy", HttpStatusCode.TooManyRequests) : StubHttpHandler.Json($"[{Output(0)}]"));
        using RetryReadOnce retry = new(stub);
        using Blockfrost provider = new("CANARY-test-project", handler: retry);
        Assert.Single(await provider.GetUtxosAsync(Address, timeout.Token));
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.SubmitTransactionAsync(Transaction, timeout.Token));
        Assert.True(error.IsTransient);
        Assert.Contains("the transaction may have been accepted", error.Message, StringComparison.Ordinal);
        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Post], stub.Requests.Select(r => r.Method));
    }

    [Theory]
    [InlineData("CANARY-block-key")]
    [InlineData("CANARY-block-path")]
    [InlineData("CANARY-block-query")]
    [InlineData("CANARY-block-password")]
    [InlineData("CANARY-block-host")]
    [InlineData("Q6L C5R")]
    public async Task MasksEachConfiguredCredential(string token)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string? text = null;
        using StubHttpHandler stub = new(_ => text is null ? throw new IOException(token) : StubHttpHandler.Json(text, HttpStatusCode.Forbidden));
        using Blockfrost provider = new("CANARY-block-key", url: "https://test:CANARY-block-password@CANARY-block-host.test/CANARY-block-path/?token=CANARY-block-query&form=Q6L+C5R", handler: stub);
        foreach (string body in new[] { token + "#", new string('z', 1010) + token, "#" + token })
        {
            text = body;
            ProviderStatusException status = await Assert.ThrowsAsync<ProviderStatusException>(() => provider.GetParametersAsync(timeout.Token));
            Assert.Contains('*', status.Detail);
            ProviderHttpTests.AssertNoCanary(status, token);
        }
        text = null;
        ProviderHttpTests.AssertNoCanary(await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token)), token);
    }

    private sealed class RetryReadOnce(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage first = await base.SendAsync(request, cancellationToken);
            if (request.Method != HttpMethod.Get || first.StatusCode != HttpStatusCode.TooManyRequests) { return first; }
            first.Dispose();
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
