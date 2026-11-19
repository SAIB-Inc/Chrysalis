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
        Assert.Equal(new string('a', 64), await provider.SubmitTransactionAsync(tx));
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
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.SubmitTransactionAsync(TestData.Transaction()));
        Assert.Null(error.StatusCode);
        Assert.Null(error.InnerException);
    }
}
