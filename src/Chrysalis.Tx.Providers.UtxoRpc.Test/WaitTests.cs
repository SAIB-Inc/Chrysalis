using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Grpc.Core;
using Utxorpc.V1Beta.Query;
using Utxorpc.V1Beta.Submit;
using Xunit;
using QueryChainTx = Utxorpc.V1Beta.Query.AnyChainTx;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises streaming confirmation, the one historical lookup and call lifetime.</summary>
public sealed class WaitTests
{
    private static readonly string Hash = new('a', 64);

    /// <summary>Only confirmation of the requested hash ends the wait, disposing the stream.</summary>
    /// <returns>A task for the ordered stages and server-side closure.</returns>
    [Fact]
    public async Task WaitsForMatchingConfirmationAndClosesTheStream()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Submit.WaitHandler = async (request, stream, context) =>
        {
            Assert.True(Convert.FromHexString(Hash).AsSpan().SequenceEqual(Assert.Single(request.Ref).Span));
            Assert.Equal("CANARY-stream", context.RequestHeaders.GetValue("x-test-token"));
            Assert.Equal(DateTime.MaxValue, context.Deadline);
            try
            {
                await stream.WriteAsync(new WaitForTxResponse { Ref = ByteString.CopyFrom(new byte[32]), Stage = Stage.Confirmed });
                await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Mempool });
                await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Network });
                await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Confirmed });
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            finally { _ = closed.TrySetResult(); }
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod, new Dictionary<string, string> { ["x-test-token"] = "CANARY-stream" });
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await provider.WaitForConfirmationAsync(Hash, timeout.Token);
        await closed.Task.WaitAsync(timeout.Token);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>An orderly end without matching confirmation is an unusable answer.</summary>
    /// <returns>A task for the premature end.</returns>
    [Fact]
    public async Task RejectsAnEarlyEnd()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.WaitForConfirmationAsync(Hash, CancellationToken.None));
        Assert.Null(error.StatusCode);
        Assert.Equal("WaitForTx: The stream ended before confirmation.", error.Message);
    }

    /// <summary>Caller cancellation retains the token and the transport cause.</summary>
    /// <returns>A task for a cancelled stream.</returns>
    [Fact]
    public async Task PreservesCancellation()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Submit.WaitHandler = async (_, _, context) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        using CancellationTokenSource cancellation = new();
        Task wait = provider.WaitForConfirmationAsync(Hash, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
    }

    /// <summary>An initially unknown pending stage performs exactly one historical lookup.</summary>
    /// <param name="found">Whether the transaction is already on chain.</param>
    /// <returns>A task for the historical fallback.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LooksUpOnlyTheFirstUnspecifiedStage(bool found)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (request, _) =>
        {
            Assert.Equal(Hash, Convert.ToHexStringLower(request.Hash.Span));
            return found ? Task.FromResult(new ReadTxResponse { Tx = new QueryChainTx { NativeBytes = ByteString.CopyFrom([0x80]) } })
                : throw new RpcException(new Status(StatusCode.NotFound, "unknown transaction"));
        };
        fake.Submit.WaitHandler = async (request, stream, context) =>
        {
            await stream.WriteAsync(new WaitForTxResponse { Ref = ByteString.CopyFrom(new byte[32]), Stage = Stage.Unspecified });
            await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Unspecified });
            if (!found)
            {
                await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Unspecified });
                await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Confirmed });
            }
            else { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await provider.WaitForConfirmationAsync(Hash, timeout.Token);
        _ = Assert.Single(fake.Requests.OfType<ReadTxRequest>());
        Assert.Equal(2, fake.RequestCount);
    }

    /// <summary>A later unspecified stage never starts a lookup.</summary>
    /// <returns>A task for an already pending transaction.</returns>
    [Fact]
    public async Task DoesNotLookUpAfterTheFirstMatchingStage()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.WaitHandler = async (request, stream, _) =>
        {
            await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Mempool });
            await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Unspecified });
            await stream.WriteAsync(new WaitForTxResponse { Ref = request.Ref[0], Stage = Stage.Confirmed });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        await provider.WaitForConfirmationAsync(Hash, CancellationToken.None);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>Streaming failures use the same error contract as unary failures.</summary>
    /// <returns>A task for a refused stream.</returns>
    [Fact]
    public async Task PreservesStatusWithoutRetrying()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.WaitHandler = (_, _, _) => throw new RpcException(new Status(StatusCode.Unavailable, "stream unavailable"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.WaitForConfirmationAsync(Hash, CancellationToken.None));
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal("WaitForTx failed: Unavailable", error.Message);
        Assert.Equal("stream unavailable", error.Detail);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>Invalid hashes never reach the server.</summary>
    /// <returns>A task for argument validation.</returns>
    [Fact]
    public async Task RejectsInvalidHashesBeforeCalling()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => provider.WaitForConfirmationAsync(null!, CancellationToken.None));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => provider.WaitForConfirmationAsync("aa", CancellationToken.None));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => provider.WaitForConfirmationAsync(new string('z', 64), CancellationToken.None));
        Assert.Equal(0, fake.RequestCount);
    }
}
