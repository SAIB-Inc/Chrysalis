using Chrysalis.Codec.Serialization;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;
using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Grpc.Core;
using Utxorpc.V1Beta.Query;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises auxiliary-data envelopes and absence/error contracts.</summary>
public sealed class MetadataTests
{
    /// <summary>A constructed transaction carries its metadata, or has no auxiliary data.</summary>
    /// <param name="present">Whether to include metadata.</param>
    /// <returns>A task for the metadata read.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsMetadataOrAbsence(bool present)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (request, _) =>
        {
            Assert.Equal(TestData.Hash, Convert.ToHexStringLower(request.Hash.Span));
            return Task.FromResult(new ReadTxResponse { Tx = new AnyChainTx { NativeBytes = ByteString.CopyFrom(CborSerializer.Serialize(TestData.Transaction(present))) } });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        LedgerMetadata? result = await provider.GetTransactionMetadataAsync(TestData.Hash, CancellationToken.None);
        if (present) { Assert.True(Assert.IsType<LedgerMetadata>(result).Value.ContainsKey(674)); }
        else { Assert.Null(result); }
    }

    /// <summary>Early and indefinite transaction arrays expose the auxiliary item without decoding the body.</summary>
    /// <param name="hex">A minimal transaction envelope.</param>
    /// <param name="present">Whether the envelope carries metadata.</param>
    /// <returns>A task for the envelope check.</returns>
    [Theory]
    [InlineData("83a0a0a1182a6161", true)]
    [InlineData("83a0a082a1182a616180", true)]
    [InlineData("9fa0a0f5d90103a100a1182a6161ff", true)]
    [InlineData("82a0a0", false)]
    [InlineData("9fa0a0ff", false)]
    [InlineData("83a0a0f6", false)]
    [InlineData("84a0a0f5f6", false)]
    [InlineData("9fa0a0f5f6ff", false)]
    [InlineData("9fa0a0f6ff", false)]
    public async Task ReadsValidTransactionEnvelopes(string hex, bool present)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (_, _) => Task.FromResult(new ReadTxResponse { Tx = new AnyChainTx { NativeBytes = ByteString.CopyFrom(Convert.FromHexString(hex)) } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        LedgerMetadata? result = await provider.GetTransactionMetadataAsync(TestData.Hash);
        if (present) { Assert.Equal(42UL, Assert.Single(Assert.IsType<LedgerMetadata>(result).Value).Key); }
        else { Assert.Null(result); }
    }

    /// <summary>NOT_FOUND is absent; other statuses remain failures.</summary>
    /// <param name="status">The server status.</param>
    /// <returns>A task for the response check.</returns>
    [Theory]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Unavailable)]
    public async Task DistinguishesMissingTransactionsFromFailures(StatusCode status)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (_, _) => throw new RpcException(new Status(status, "transaction status"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        if (status == StatusCode.NotFound) { Assert.Null(await provider.GetTransactionMetadataAsync(TestData.Hash)); }
        else { Assert.Equal(status, (await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetTransactionMetadataAsync(TestData.Hash))).StatusCode); }
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>Missing, malformed or truncated envelopes are unusable responses, not absent metadata.</summary>
    /// <param name="hex">The malformed transaction bytes.</param>
    /// <returns>A task for the invalid envelope.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("84a0a0")]
    [InlineData("9fa0a0f5")]
    [InlineData("82a0a0a1182a6161")]
    [InlineData("84a0a0f5")]
    [InlineData("9fa0a0f5f6")]
    [InlineData("83a0a0a1182a616100")]
    [InlineData("9fa0a0f5f6ff00")]
    [InlineData("85a0a0f5f600")]
    [InlineData("84a0a0f6f6")]
    public async Task RejectsInvalidTransactionEnvelopes(string hex)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (_, _) => Task.FromResult(new ReadTxResponse { Tx = new AnyChainTx { NativeBytes = ByteString.CopyFrom(Convert.FromHexString(hex)) } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetTransactionMetadataAsync(TestData.Hash));
        Assert.Null(error.StatusCode);
        Assert.NotNull(error.InnerException);
        Assert.StartsWith("ReadTx: ", error.Message, StringComparison.Ordinal);
    }
}
