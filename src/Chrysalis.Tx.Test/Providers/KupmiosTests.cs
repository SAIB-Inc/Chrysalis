using System.Net;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class KupmiosTests
{
    internal static Kupmios Create(StubHttpHandler stub) => new("https://kupo.test/", "https://ogmios.test/", handler: stub);

    internal static string Match(string index = "\"output_index\":0,", string value = "\"coins\":15") =>
        $$"""{"transaction_id":"{{BlockfrostTests.Hash}}",{{index}}"address":"{{BlockfrostTests.Address}}","value":{ {{value}} } }""";

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
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(500)]
    public async Task OgmiosFailuresPreserveCodeOrStatusAndMaskDetail(int status)
    {
        const string key = "CANARY-ogmios-submit";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(status == 500 ? "not JSON"
            : $$"""{"jsonrpc":"2.0","error":{"code":3117,"message":"{{key}}"} }""", (HttpStatusCode)status));
        using Kupmios provider = new("https://kupo.test/", $"https://ogmios.test/{key}/", handler: stub);
        ProviderException error = await Assert.ThrowsAnyAsync<ProviderException>(() => provider.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
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
    [InlineData("negative coins")]
    [InlineData("negative index")]
    [InlineData("missing script language")]
    [InlineData("missing script bytes")]
    [InlineData("unknown script language")]
    [InlineData("missing datum hash")]
    [InlineData("null inline datum")]
    [InlineData("empty inline datum")]
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
            "negative coins" => Match(value: "\"coins\":-1"),
            "negative index" => Match("\"output_index\":-1,"),
            "missing script language" => With(",\"script\":{\"script\":\"00\"}"),
            "missing script bytes" => With(",\"script\":{\"language\":\"plutus:v1\"}"),
            "unknown script language" => With(",\"script\":{\"language\":\"plutus:v9\",\"script\":\"00\"}"),
            "missing datum hash" => With(",\"datum_type\":\"hash\""),
            "null inline datum" => With($""", "datum_type":"inline", "datum_hash":"{BlockfrostTests.Hash}", "datum":null"""),
            "empty inline datum" => With(",\"datum_type\":\"inline\",\"datum\":\"\""),
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
}
