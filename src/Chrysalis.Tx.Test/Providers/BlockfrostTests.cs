using System.Net;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

public class BlockfrostTests
{
    internal static string Hash { get; } = new('0', 64);
    internal static string Address { get; } = Chrysalis.Wallet.Models.Addresses.Address.FromBytes([0x60, .. new byte[28]]).ToBech32();
    internal static ITransaction Transaction { get; } = new TransactionBuilder().Build();

    internal static string Output(int index) => System.Text.Json.JsonSerializer.Serialize(new
    {
        tx_hash = Hash,
        tx_index = index,
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

    [Fact]
    public async Task BadLabelDoesNotGivePartialMetadata()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("""[{"label":"0","json_metadata":1},{"label":"x","json_metadata":2}]"""));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetTransactionMetadataAsync(Hash, timeout.Token));
        Assert.False(error.IsTransient);
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
            if (scenario == 0) { outputs[0] = outputs[0][..^1] + ",\"reference_script_hash\":\"script\"}"; }
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
                 "max_block_ex_mem":"0","max_block_ex_steps":"0","max_val_size":"0","coins_per_utxo_size":"0"}
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
        using StubHttpHandler stub = new(request => StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal)
            ? "[]" : """{"outputs":[{"output_index":1}]}"""));
        using Blockfrost provider = new("CANARY-test-project", handler: stub);
        Assert.Null(await provider.GetUtxoByOutRefAsync(Hash, 0, timeout.Token));
        Assert.Null(await provider.GetTransactionMetadataAsync(Hash, timeout.Token));
        Assert.Equal(2, stub.Requests.Count);
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
