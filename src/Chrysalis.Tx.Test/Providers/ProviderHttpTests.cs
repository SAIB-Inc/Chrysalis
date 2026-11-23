using System.Collections;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Chrysalis.Tx.Providers;
using SAIB.Cbor;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class ProviderHttpTests
{
    private const string Canary = "CANARY-diagnostic-fragment";

    internal static void AssertNoCanary(Exception error, params string[] secrets)
    {
        foreach (string secret in secrets)
        {
            int width = Math.Min(8, secret.Length);
            for (int i = 0; i <= secret.Length - width; i++)
            {
                string piece = secret.Substring(i, width);
                Assert.DoesNotContain(piece, error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(piece, error.ToString(), StringComparison.OrdinalIgnoreCase);
                if (error is ProviderException failure) { Assert.DoesNotContain(piece, failure.Detail, StringComparison.OrdinalIgnoreCase); }
                foreach (DictionaryEntry entry in error.Data)
                {
                    Assert.DoesNotContain(piece, entry.Key.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(piece, entry.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        if (error is AggregateException aggregate)
        {
            foreach (Exception child in aggregate.InnerExceptions) { AssertNoCanary(child, secrets); }
        }
        else if (error.InnerException is { } inner) { AssertNoCanary(inner, secrets); }
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(402, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(408, true)]
    [InlineData(418, false)]
    [InlineData(425, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public async Task StatusHasFixedSafeFacts(int status, bool transient)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => new((HttpStatusCode)status)
        {
            Content = new StringContent(Canary),
            ReasonPhrase = Canary
        });
        using HttpClient client = new(stub, disposeHandler: false);
        using HttpRequestMessage request = new(HttpMethod.Get, "https://stub.test/");
        ProviderHttp http = new(client, "Test", [Canary]);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => http.SendAsync(request, "Read", null, timeout.Token));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal(transient, error.IsTransient);
        Assert.Equal(new string('*', Canary.Length), error.Detail);
        Assert.Null(error.InnerException);
        Assert.Empty(error.Data);
        AssertNoCanary(error, Canary);
    }

    [Theory]
    [InlineData(0, true, nameof(HttpRequestException))]
    [InlineData(1, true, nameof(IOException))]
    [InlineData(2, true, nameof(TaskCanceledException))]
    [InlineData(3, false, nameof(InvalidOperationException))]
    [InlineData(4, false, nameof(ArgumentException))]
    [InlineData(5, true, nameof(InvalidOperationException))]
    public async Task HandlerFailuresDoNotRetainCauses(int kind, bool transient, string type)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => throw kind switch
        {
            0 => new HttpRequestException(HttpRequestError.ConnectionError, Canary),
            1 => new IOException(Canary),
            2 => new TaskCanceledException(Canary),
            3 => new InvalidOperationException(Canary),
            4 => new ArgumentException(Canary),
            _ => new InvalidOperationException(Canary, new IOException(Canary))
        });
        using HttpClient client = new(stub, disposeHandler: false);
        using HttpRequestMessage request = new(HttpMethod.Get, "https://stub.test/");
        ProviderHttp http = new(client, "Test", [Canary]);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => http.SendAsync(request, "Read", null, timeout.Token));
        Assert.Contains(type, error.Message, StringComparison.Ordinal);
        if (kind == 0) { Assert.Contains("ConnectionError", error.Message, StringComparison.Ordinal); }
        Assert.Equal(transient, error.IsTransient);
        Assert.Empty(error.Detail);
        Assert.Empty(error.Data);
        Assert.Null(error.InnerException);
        AssertNoCanary(error, Canary);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("future", 86400)]
    [InlineData("past", 0)]
    [InlineData("invalid", -1)]
    public async Task RetryAfterKeepsStatus(string value, int seconds)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ =>
        {
            HttpResponseMessage reply = StubHttpHandler.Json("failure", HttpStatusCode.TooManyRequests);
            Assert.True(reply.Headers.TryAddWithoutValidation("Retry-After", value switch
            {
                "future" => DateTimeOffset.UtcNow.AddDays(1).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                "past" => "Sat, 01 Jan 2000 00:00:00 GMT",
                _ => value
            }));
            return reply;
        });
        using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
        ProviderHttp http = new(client, "Test", []);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => http.GetJsonAsync<string>("read", "Read", null, timeout.Token));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        if (seconds == -1) { Assert.Null(error.RetryAfter); }
        else if (seconds == 86400) { Assert.InRange(error.RetryAfter!.Value.TotalHours, 23, 24); }
        else { Assert.Equal(TimeSpan.FromSeconds(seconds), error.RetryAfter); }
    }

    [Fact]
    public async Task ErrorReadIsBoundedInBytesAndDisposes()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        int position = 0;
        byte[] multibyte = "€"u8.ToArray();
        using StubBody body = new((buffer, _) =>
        {
            for (int i = 0; i < buffer.Length; i++) { buffer.Span[i] = multibyte[position++ % multibyte.Length]; }
            return ValueTask.FromResult(buffer.Length);
        });
        using StubHttpHandler stub = new(_ => new(HttpStatusCode.TooManyRequests) { Content = new StreamContent(body) });
        using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
        ProviderHttp http = new(client, "Test", [Canary]);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => http.GetJsonAsync<string>("read", "Read", null, timeout.Token));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(4 * (1024 + Canary.Length - 1), body.BytesRead);
        Assert.Equal(body.BytesRead, body.LargestReadEnd);
        Assert.Equal(1024, error.Detail.Length);
        Assert.True(body.WasDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CutsKeepErrorStatusButNotPartialSuccess(bool success)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubBody body = StubBody.BreaksAfterPrefix();
        using StubHttpHandler stub = new(_ => new(success ? HttpStatusCode.OK : HttpStatusCode.Forbidden) { Content = new StreamContent(body) });
        using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
        ProviderHttp http = new(client, "Test", [Canary]);
        ProviderException error = await Assert.ThrowsAnyAsync<ProviderException>(() => http.GetJsonAsync<List<int>>("read", "Read", null, timeout.Token));
        if (!success) { Assert.Equal(HttpStatusCode.Forbidden, Assert.IsType<ProviderStatusException>(error).StatusCode); }
        Assert.Equal(success, error.IsTransient);
        Assert.Empty(error.Detail);
        Assert.Null(error.InnerException);
        AssertNoCanary(error, "CANARY-broken-body");
        Assert.True(body.WasDisposed);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("<html>")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task BadJsonIsNeverAbsence(string json)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(json));
        using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
        ProviderHttp http = new(client, "Test", []);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => http.GetJsonAsync<List<int>>("read", "Read", null, timeout.Token));
        Assert.False(error.IsTransient);
        Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingReadsRespectBodyDeadlineAndCallerCancellation(bool caller)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using StubBody body = StubBody.Pending(entered);
        using StubHttpHandler stub = new(_ => new(caller ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StreamContent(body) });
        using HttpClient client = new(stub, disposeHandler: false)
        {
            BaseAddress = new("https://stub.test/"),
            Timeout = caller ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(50)
        };
        ProviderHttp http = new(client, "Test", []);
        Task read = http.GetJsonAsync<List<int>>("read", "Read", null, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        if (caller)
        {
            await timeout.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.Equal(timeout.Token, error.CancellationToken);
            Assert.Null(error.InnerException);
        }
        else { Assert.True((await Assert.ThrowsAsync<ProviderException>(() => read)).IsTransient); }
        Assert.True(body.WasDisposed);
    }

    [Theory]
    [InlineData("https://stub.test/CANARYpath/", "CANARYpath", false)]
    [InlineData("https://stub.test/?token=CANARYquery", "CANARYquery", false)]
    [InlineData("https://alice:CANARYpassword@stub.test/", "CANARYpassword", false)]
    [InlineData("https://CANARYhost.stub.test/", "CANARYHOST", false)]
    [InlineData("https://stub.test/?token=CANARY%2Fencoded", "CANARY%2Fencoded", false)]
    [InlineData("https://stub.test/?token=CANARY%2Fencoded", "CANARY/encoded", false)]
    [InlineData("Bearer CANARYbearer", "CANARYbearer", true)]
    [InlineData("Basic CANARYbasic", "CANARYbasic", true)]
    [InlineData("id=x; password=CANARYpair", "CANARYpair", true)]
    [InlineData("CANARYraw", "CANARYraw", true)]
    public void RedactionMasksEachCredentialAndACutPrefix(string configured, string echoed, bool header)
    {
        string[] secrets = [.. header ? ProviderHttp.SecretsOfHeader(configured) : ProviderHttp.SecretsOf(new Uri(configured))];
        foreach (string body in new[] { echoed + "!", new string('x', 1021) + echoed, "!" + echoed, "!" + echoed[..6] })
        {
            string masked = ProviderHttp.Redact(body, secrets);
            Assert.DoesNotContain(echoed[..6], masked, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(echoed, masked, StringComparison.OrdinalIgnoreCase);
            Assert.Contains('*', masked);
            Assert.True(masked.Length <= 1024);
        }
        Assert.Equal("api/v0", ProviderHttp.Redact("api/v0", ["", .. ProviderHttp.SecretsOf(new Uri("https://api.test/v0/"))]));
    }

    [Theory]
    [InlineData("CANARY7")]
    [InlineData("~")]
    public async Task ShortCredentialsInEverySlotAreMasked(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        IEnumerable<string>[] sources =
        [
            [token],
            ProviderHttp.SecretsOfHeader($"Bearer {token}"),
            ProviderHttp.SecretsOfHeader($"Basic {token}"),
            ProviderHttp.SecretsOfHeader($"id=test; value={token}"),
            ProviderHttp.SecretsOfHeader(token),
            ProviderHttp.SecretsOf(new Uri($"https://stub.test/?token={token}")),
            ProviderHttp.SecretsOf(new Uri($"https://user:{token}@stub.test/")),
            ProviderHttp.SecretsOf(new Uri($"https://{token}:pass@stub.test/"))
        ];
        foreach (IEnumerable<string> secrets in sources)
        {
            using StubHttpHandler stub = new(_ => StubHttpHandler.Json(token, HttpStatusCode.Forbidden));
            using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
            ProviderHttp http = new(client, "Test", [.. secrets]);
            ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => http.GetJsonAsync<string>("read", "Read", null, timeout.Token));
            Assert.Equal(new string('*', token.Length), error.Detail);
            AssertNoCanary(error, token);
            Assert.Null(error.InnerException);
            Assert.Empty(error.Data);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task MapTurnsDecodeFailuresIntoUnusable(int kind)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("""[{"label":"1","metadata":"bfff"}]"""));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        ProviderException error = kind == 4
            ? await Assert.ThrowsAsync<ProviderException>(() => provider.GetTransactionMetadataAsync(BlockfrostTests.Hash, timeout.Token))
            : Assert.Throws<ProviderException>(() => ProviderHttp.Map<int, int>(0, _ => throw kind switch
            {
                0 => new FormatException("x"),
                1 => new JsonException("x"),
                2 => new CborException("x"),
                _ => new InvalidDataException("x")
            }, "P", "Op"));
        Assert.False(error.IsTransient);
        Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task LateCancellationAfterSubmitJsonBodyReturnsResult()
    {
        using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
        byte[] payload = "\"accepted-tx-id\""u8.ToArray();
        int step = 0;
        using StubBody body = new(async (buffer, _) =>
        {
            if (step++ == 0)
            {
                payload.CopyTo(buffer.Span);
                return payload.Length;
            }
            await caller.CancelAsync();
            return 0;
        });
        using StubHttpHandler stub = new(_ => new(HttpStatusCode.OK) { Content = new StreamContent(body) });
        using HttpClient client = new(stub, disposeHandler: false) { BaseAddress = new("https://stub.test/") };
        using HttpRequestMessage request = new(HttpMethod.Post, "submit");
        ProviderHttp http = new(client, "Test", []);
        string result = await http.SubmitAsync(request, "SubmitTransactionAsync",
            (response, ct) => http.ReadJsonAsync<string>(response, "SubmitTransactionAsync", ct), jsonRpc: false, caller.Token);
        Assert.Equal("accepted-tx-id", result);
        Assert.True(caller.IsCancellationRequested);
        Assert.True(body.WasDisposed);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ChannelClosedWithoutInnerIsTransientAndWithNonTransientInnerIsNot(bool hasInner, bool expectedTransient)
    {
        ChannelClosedException error = hasInner
            ? new ChannelClosedException(new InvalidDataException("x"))
            : new ChannelClosedException();
        ProviderException failure = Assert.IsType<ProviderException>(
            ProviderHttp.TransportFailure("Ouroboros", "GetParametersAsync", error, CancellationToken.None));
        Assert.Equal(expectedTransient, failure.IsTransient);
        Assert.Empty(failure.Detail);
        Assert.Null(failure.InnerException);
    }
}
