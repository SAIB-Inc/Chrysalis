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
    /// <returns>A task for the failed call.</returns>
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.ResourceExhausted)]
    public async Task PreservesStatusWithoutRetrying(StatusCode status)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = (_, _) => throw new RpcException(new Status(status, "test server detail"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, new Dictionary<string, string> { ["x-test-token"] = "CANARY-hidden" });
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(CancellationToken.None));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal($"ReadParams failed: {status}: test server detail", error.Message);
        _ = Assert.IsType<RpcException>(error.InnerException);
        Assert.DoesNotContain("CANARY-hidden", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>Cancellation during a call retains the caller's token and the gRPC cause.</summary>
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
        using CancellationTokenSource cancellation = new();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Task call = provider.GetParametersAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        _ = Assert.IsType<RpcException>(error.InnerException);
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
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, timeout: TimeSpan.FromMilliseconds(200));
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(CancellationToken.None));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        RpcException cause = Assert.IsType<RpcException>(error.InnerException);
        output.WriteLine("Deadline detail: [{0}]", cause.Status.Detail);
        Assert.Equal($"ReadParams failed: DeadlineExceeded: {cause.Status.Detail}", error.Message);
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
