using Chrysalis.Wallet.Models.Enums;
using Grpc.Core;
using Xunit;
using Xunit.Abstractions;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises metadata, gRPC failures, deadlines and channel lifetime.</summary>
/// <param name="output">Receives the runtime deadline detail for diagnostic evidence.</param>
public sealed class TransportTests(ITestOutputHelper output)
{
    /// <summary>Configured metadata reaches the server and network selection is retained.</summary>
    /// <returns>A task for the header check.</returns>
    [Fact]
    public async Task SendsConfiguredHeaders()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        string? seen = null;
        fake.Handler = (_, context) =>
        {
            seen = context.RequestHeaders.GetValue("x-test-token");
            return Task.FromResult(FakeUtxoRpc.Answer(new()));
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, new Dictionary<string, string> { ["x-test-token"] = "CANARY=test" });
        _ = await provider.GetParametersAsync(CancellationToken.None);
        Assert.Equal("CANARY=test", seen);
        Assert.Equal(NetworkType.Preprod, provider.NetworkType);
    }

    /// <summary>Server status and detail are preserved without a retry or metadata disclosure.</summary>
    /// <param name="status">The server's gRPC status.</param>
    /// <param name="transient">Whether a later read might succeed.</param>
    /// <returns>A task for the failed call.</returns>
    [Theory]
    [InlineData(StatusCode.Unavailable, true)]
    [InlineData(StatusCode.Unauthenticated, false)]
    [InlineData(StatusCode.PermissionDenied, false)]
    [InlineData(StatusCode.ResourceExhausted, true)]
    [InlineData(StatusCode.Aborted, true)]
    public async Task PreservesStatusWithoutRetrying(StatusCode status, bool transient)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = (_, _) => throw new RpcException(new Status(status, "test server detail"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(timeout.Token));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(transient, error.IsTransient);
        Assert.Equal($"ReadParams failed: {status}", error.Message);
        Assert.Equal("test server detail", error.Detail);
        Assert.Null(error.InnerException);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>The server can echo either a whole header or the token on its own.</summary>
    [Theory]
    [InlineData("x-test-token", "", "CANARY-plain-header")]
    [InlineData("authorization", "Bearer ", "CANARY-bearer-header")]
    [InlineData("authorization", "Basic ", "CANARY-basic-header")]
    public async Task MasksHeaderTokensAlone(string name, string scheme, string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, new Dictionary<string, string> { [name] = scheme + token });
        foreach (string text in new[] { token + "#", new string('z', 1018) + token, "#" + token })
        {
            fake.Handler = (_, _) => throw new RpcException(new Status(StatusCode.PermissionDenied, text));
            UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(timeout.Token));
            Assert.Equal("ReadParams failed: PermissionDenied", error.Message);
            Assert.Contains('*', error.Detail);
            foreach (int i in Enumerable.Range(0, token.Length - 7))
            {
                string part = token.Substring(i, 8);
                Assert.DoesNotContain(part, error.Detail, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(part, error.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            Assert.Null(error.InnerException);
            Assert.Empty(error.Data);
        }
        Assert.Equal(3, fake.RequestCount);
    }

    /// <summary>Cancellation during a call retains only the caller's token.</summary>
    /// <returns>A task for the cancelled call.</returns>
    [Fact]
    public async Task PreservesCallerCancellation()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Handler = async (_, context) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            return FakeUtxoRpc.Answer(new());
        };
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Task call = provider.GetParametersAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>A slow handler exceeds the configured 200 ms deadline.</summary>
    /// <returns>A task for the deadline failure.</returns>
    [Fact]
    public async Task EnforcesTheUnaryDeadline()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = async (_, context) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
            return FakeUtxoRpc.Answer(new());
        };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, timeout: TimeSpan.FromMilliseconds(200));
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(timeout.Token));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        Assert.True(error.IsTransient);
        Assert.Equal("ReadParams failed: DeadlineExceeded", error.Message);
        Assert.Null(error.InnerException);
        output.WriteLine("Deadline detail: [{0}]", error.Detail);
    }

    /// <summary>Invalid endpoints, header names and timeouts fail before any call.</summary>
    [Fact]
    public void RejectsInvalidConstructorArguments()
    {
        Uri valid = new("http://127.0.0.1:1");
        _ = Assert.Throws<ArgumentNullException>(() => new UtxoRpc(null!, NetworkType.Preprod));
        _ = Assert.Throws<ArgumentException>(() => new UtxoRpc(new Uri("relative", UriKind.Relative), NetworkType.Preprod));
        _ = Assert.Throws<ArgumentException>(() => new UtxoRpc(new Uri("ftp://example.invalid"), NetworkType.Preprod));
        _ = Assert.ThrowsAny<ArgumentException>(() => new UtxoRpc(valid, NetworkType.Preprod, new Dictionary<string, string> { ["bad header"] = "CANARY" }));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new UtxoRpc(valid, NetworkType.Preprod, timeout: TimeSpan.Zero));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new UtxoRpc(valid, NetworkType.Preprod, timeout: TimeSpan.FromTicks(-1)));
    }

    /// <summary>Repeated disposal is safe and further calls throw ObjectDisposedException.</summary>
    /// <returns>A task for the disposed-channel check.</returns>
    [Fact]
    public async Task RejectsCallsAfterRepeatedDisposal()
    {
        using UtxoRpc provider = new(new Uri("http://127.0.0.1:1"), NetworkType.Preprod);
        provider.Dispose();
        provider.Dispose();
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetParametersAsync(CancellationToken.None));
    }
}
