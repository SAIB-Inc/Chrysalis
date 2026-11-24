using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Grpc.Core;
using Utxorpc.V1Beta.Submit;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises exact transaction submission and rejection without retries.</summary>
public sealed class SubmitTests
{
    /// <summary>The server receives the transaction bytes unchanged and returns lower-case hex.</summary>
    /// <returns>A task for submission.</returns>
    [Fact]
    public async Task SendsTheSerializedTransactionOnce()
    {
        ITransaction tx = TestData.Transaction();
        byte[] expected = CborSerializer.Serialize(tx);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.Handler = (request, _) =>
        {
            Assert.True(expected.AsSpan().SequenceEqual(request.Tx.Raw.Span));
            return Task.FromResult(new SubmitTxResponse { Ref = ByteString.CopyFrom(Convert.FromHexString(new string('A', 64))) });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Assert.Equal(new string('a', 64), await provider.SubmitTransactionAsync(tx, TestContext.Current.CancellationToken));
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>A rejected submission retains status and server detail without sending again.</summary>
    /// <returns>A task for rejection.</returns>
    [Fact]
    public async Task NeverRetriesRejectedSubmission()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.Handler = (_, _) => throw new RpcException(new Status(StatusCode.InvalidArgument, "submission rejected"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.SubmitTransactionAsync(TestData.Transaction(), CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal("SubmitTx failed: InvalidArgument", error.Message);
        Assert.Equal("submission rejected", error.Detail);
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>A short returned reference is unusable, not a transaction hash.</summary>
    /// <returns>A task for the response check.</returns>
    [Fact]
    public async Task RejectsShortSubmissionReference()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.Handler = (_, _) => Task.FromResult(new SubmitTxResponse { Ref = ByteString.CopyFrom(new byte[31]) });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.SubmitTransactionAsync(TestData.Transaction(), TestContext.Current.CancellationToken));
        Assert.Null(error.StatusCode);
        Assert.Null(error.InnerException);
    }

    /// <summary>A cancel that arrives after the server accepted the transaction returns its reference.</summary>
    /// <returns>A task for late cancellation.</returns>
    [Fact]
    public async Task LateCancellationAfterSubmitAcceptanceReturnsTransactionId()
    {
        TaskCompletionSource answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.Handler = async (_, context) =>
        {
            await answer.Task.WaitAsync(context.CancellationToken);
            return new SubmitTxResponse { Ref = ByteString.CopyFrom(Convert.FromHexString(new string('A', 64))) };
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        using CancellationTokenSource caller = new();
        ITransaction tx = TestData.Transaction();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CancelOnResume(caller));
        Task<string> submission = provider.SubmitTransactionAsync(tx, caller.Token);
        SynchronizationContext.SetSynchronizationContext(previous);
        answer.SetResult();
        Assert.Equal(new string('a', 64), await submission.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(1, fake.RequestCount);
    }

    // Cancels the caller just before the provider resumes after the server's answer: the latest cancel possible.
    private sealed class CancelOnResume(CancellationTokenSource caller) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => base.Post(_ => { caller.Cancel(); d(state); }, null);
    }
}
