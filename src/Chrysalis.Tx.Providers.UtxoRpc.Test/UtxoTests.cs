using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Xunit.Sdk;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using Grpc.Core;
using Google.Protobuf;
using Utxorpc.V1Beta.Query;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises paging, address batching, references and unusable responses.</summary>
public sealed class UtxoTests
{
    /// <summary>Only a script omitted by Blockfrost may be absent from the comparison.</summary>
    [Fact]
    public void ReferenceScriptParityRejectsAProviderDrop()
    {
        TransactionInput reference = TransactionInput.Create(new byte[32], 0);
        Address address = Address.Create(TestData.AddressBytes());
        Lovelace amount = Lovelace.Create(10);
        CborEncodedValue script = new(CborSerializer.Serialize<IScript>(PlutusV1Script.Create(1, new byte[] { 0 })));
        ResolvedInput present = new(reference, PostAlonzoTransactionOutput.Create(address, amount, null, script));
        ResolvedInput absent = new(reference, PostAlonzoTransactionOutput.Create(address, amount, null, null));
        _ = Assert.Throws<NotNullException>(() => ParityTests.EqualOutputs([present], [absent]));
        ParityTests.EqualOutputs([absent], [present]);
        ParityTests.EqualOutputs([present], [present]);
    }

    /// <summary>A moving read refreshes only a disappearing reference and is bounded to three snapshots.</summary>
    /// <param name="vanishingSnapshots">How many searches lose their output before lookup.</param>
    /// <returns>A task for the same helper used by the live test.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LiveReferenceChecksRefreshOnlyBoundedSnapshots(int vanishingSnapshots)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        uint snapshots = 0;
        fake.SearchHandler = (_, _) => Task.FromResult(new SearchUtxosResponse { Items = { TestData.Output(++snapshots) } });
        fake.UtxosHandler = (request, _) => Task.FromResult(request.Keys[0].Index <= vanishingSnapshots
            ? new ReadUtxosResponse() : new ReadUtxosResponse { Items = { TestData.Output(request.Keys[0].Index) } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        if (vanishingSnapshots == 3)
        {
            _ = await Assert.ThrowsAsync<FailException>(() => LiveReadTests.RequireSnapshotReferencesAsync(provider, [TestData.Address()]));
        }
        else { await LiveReadTests.RequireSnapshotReferencesAsync(provider, [TestData.Address()]); }
        Assert.Equal((uint)Math.Min(vanishingSnapshots + 1, 3), snapshots);
        Assert.Equal((int)snapshots * 2, fake.RequestCount);
    }

    /// <summary>Three pages preserve order and carry each token to the next request.</summary>
    /// <returns>A task for paging.</returns>
    [Fact]
    public async Task ReadsThreePagesInOrder()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        int page = 0;
        fake.SearchHandler = (request, _) =>
        {
            Assert.False(request.HasMaxItems);
            Assert.Equal(page == 0 ? "" : $"page-{page}", request.StartToken);
            Assert.True(request.Predicate.Match.Cardano.Address.ExactAddress.Span.SequenceEqual(TestData.AddressBytes()));
            page++;
            SearchUtxosResponse response = new() { Items = { TestData.Output((uint)page) } };
            if (page < 3) { response.NextToken = $"page-{page}"; }
            return Task.FromResult(response);
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        List<ResolvedInput> values = await provider.GetUtxosAsync([TestData.Address()], CancellationToken.None);
        Assert.Equal(new ulong[] { 1, 2, 3 }, values.Select(value => value.Outref.Index));
        Assert.Equal(3, fake.RequestCount);
    }

    /// <summary>An absent or empty next token ends paging after the current page.</summary>
    /// <param name="empty">Whether to explicitly send an empty token.</param>
    /// <returns>A task for the terminator check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopsAtEitherTokenTerminator(bool empty)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (_, _) =>
        {
            SearchUtxosResponse response = new();
            if (empty) { response.NextToken = ""; }
            return Task.FromResult(response);
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Assert.Empty(await provider.GetUtxosAsync([TestData.Address()], CancellationToken.None));
        Assert.Equal(1, fake.RequestCount);
    }

    /// <summary>A failure or an echoed token on page two never returns page one's partial data.</summary>
    /// <param name="repeat">Whether the server echoes instead of returning an error.</param>
    /// <returns>A task for the failed read.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailsTheWholePagedRead(bool repeat)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (request, _) =>
        {
            if (request.HasStartToken && !repeat) { throw new RpcException(new Status(StatusCode.Unavailable, "page failure")); }
            return Task.FromResult(new SearchUtxosResponse { Items = { TestData.Output() }, NextToken = "next" });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetUtxosAsync([TestData.Address()], CancellationToken.None));
        if (repeat) { Assert.Null(error.StatusCode); Assert.Null(error.InnerException); }
        else { Assert.Equal(StatusCode.Unavailable, error.StatusCode); }
        Assert.Equal(2, fake.RequestCount);
    }

    /// <summary>Repeated output identity or a cursor cycle fails the entire read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsRepeatedIdentityOrCursorCycle(bool cycle)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        int page = 0;
        fake.SearchHandler = (_, _) =>
        {
            page++;
            return Task.FromResult(new SearchUtxosResponse
            {
                Items = { TestData.Output(cycle ? (uint)page : 0) },
                NextToken = page == 2 ? "B" : "A"
            });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        ProviderException error = await Assert.ThrowsAnyAsync<ProviderException>(() => provider.GetUtxosAsync([TestData.Address()], timeout.Token));
        Assert.Contains(cycle ? "repeated start_token" : "repeated output", error.Message, StringComparison.Ordinal);
        Assert.False(error.IsTransient);
        Assert.Equal(cycle ? 3 : 2, fake.RequestCount);
    }

    /// <summary>Two outrefs in one transaction survive while repeated answers from different addresses merge.</summary>
    [Fact]
    public async Task DistinguishesIndicesAndMergesEqualAnswers()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (_, _) => Task.FromResult(new SearchUtxosResponse { Items = { TestData.Output(0), TestData.Output(1) } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        List<ResolvedInput> results = await provider.GetUtxosAsync([TestData.Address(), TestData.Address(2)], timeout.Token);
        Assert.Equal(new ulong[] { 0, 1 }, results.Select(input => input.Outref.Index));
        Assert.Equal(2, fake.RequestCount);
    }

    /// <summary>Duplicates are removed, at most five addresses run together, and completion order cannot reorder results.</summary>
    /// <returns>A task for the batch checks.</returns>
    [Fact]
    public async Task BatchesDistinctAddressesAndPreservesTheirOrder()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        int active = 0;
        int maximum = 0;
        TaskCompletionSource firstBatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.SearchHandler = async (request, context) =>
        {
            int concurrent = Interlocked.Increment(ref active);
            int seen;
            do { seen = Volatile.Read(ref maximum); }
            while (concurrent > seen && Interlocked.CompareExchange(ref maximum, concurrent, seen) != seen);
            byte marker = request.Predicate.Match.Cardano.Address.ExactAddress.Span[1];
            if (concurrent == 5) { _ = firstBatch.TrySetResult(); }
            try
            {
                if (marker <= 5) { await firstBatch.Task.WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken); }
                await Task.Delay(TimeSpan.FromMilliseconds((7 - marker) * 3), context.CancellationToken);
                return new SearchUtxosResponse { Items = { TestData.Output(marker) } };
            }
            finally { _ = Interlocked.Decrement(ref active); }
        };
        List<string> addresses = [TestData.Address(1), TestData.Address(2), TestData.Address(3), TestData.Address(1), TestData.Address(4), TestData.Address(5), TestData.Address(6)];
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        List<ResolvedInput> result = await provider.GetUtxosAsync(addresses, CancellationToken.None);
        Assert.Equal(new ulong[] { 1, 2, 3, 4, 5, 6 }, result.Select(item => item.Outref.Index));
        Assert.Equal(6, fake.RequestCount);
        Assert.Equal(5, maximum);
    }

    /// <summary>One failed address fails the whole batch.</summary>
    /// <returns>A task for the failed batch.</returns>
    [Fact]
    public async Task FailsOnOneAddress()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (request, _) => request.Predicate.Match.Cardano.Address.ExactAddress.Span[1] == 2
            ? throw new RpcException(new Status(StatusCode.Unavailable, "address failure")) : Task.FromResult(new SearchUtxosResponse());
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        _ = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetUtxosAsync([TestData.Address(1), TestData.Address(2)], CancellationToken.None));
    }

    /// <summary>Malformed bytes or references are unusable, rather than partial output values.</summary>
    /// <param name="form">The malformed component.</param>
    /// <returns>A task for the response check.</returns>
    [Theory]
    [InlineData("cbor")]
    [InlineData("empty")]
    [InlineData("hash")]
    [InlineData("reference")]
    [InlineData("field")]
    public async Task RejectsUnusableOutputs(string form)
    {
        AnyUtxoData item = TestData.Output();
        if (form == "cbor") { item.NativeBytes = ByteString.CopyFrom([0xff]); }
        if (form == "empty") { item.NativeBytes = ByteString.Empty; }
        if (form == "hash") { item.TxoRef.Hash = ByteString.CopyFrom(new byte[31]); }
        if (form == "reference") { item.TxoRef = null; }
        if (form == "field") { item.NativeBytes = ByteString.CopyFrom([0x82, 0x00, 0x01]); }
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (_, _) => Task.FromResult(new SearchUtxosResponse { Items = { item } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetUtxosAsync([TestData.Address()], CancellationToken.None));
        Assert.Null(error.StatusCode);
        Assert.Null(error.InnerException);
        Assert.StartsWith("SearchUtxos: ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The concrete overload works, and only the requested reference is returned.</summary>
    /// <param name="found">Whether the response matches the requested index.</param>
    /// <returns>A task for the lookup.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsOnlyTheAskedReference(bool found)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.UtxosHandler = (request, _) =>
        {
            Assert.Equal(TestData.Hash, Convert.ToHexStringLower(Assert.Single(request.Keys).Hash.Span));
            return Task.FromResult(new ReadUtxosResponse { Items = { TestData.Output(found ? 1u : 2u) } });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        ResolvedInput? value = await provider.GetUtxoByOutRefAsync(TestData.Hash, 1);
        Assert.Equal(found, value is not null);
    }

    /// <summary>The allocation-saving reference writer is byte-identical to the existing factory at every CBOR integer width.</summary>
    /// <param name="index">The output index at an encoding boundary.</param>
    /// <returns>A task for the public lookup.</returns>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(23u)]
    [InlineData(24u)]
    [InlineData(255u)]
    [InlineData(256u)]
    [InlineData(65535u)]
    [InlineData(65536u)]
    [InlineData(uint.MaxValue)]
    public async Task PreservesTheFactoryReferenceBytes(uint index)
    {
        AnyUtxoData item = TestData.Output(index);
        item.TxoRef.Hash = ByteString.CopyFrom(Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"));
        TransactionInput expected = TransactionInput.Create(item.TxoRef.Hash.Memory, index);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.UtxosHandler = (_, _) => Task.FromResult(new ReadUtxosResponse { Items = { item } });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        ResolvedInput result = Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(Convert.ToHexStringLower(item.TxoRef.Hash.Span), index));
        Assert.True(expected.Raw.Span.SequenceEqual(result.Outref.Raw.Span));
        Assert.True(expected.TransactionId.Span.SequenceEqual(result.Outref.TransactionId.Span));
        Assert.Equal(expected.Index, result.Outref.Index);
    }

    /// <summary>NOT_FOUND is absent, not a failed lookup.</summary>
    /// <returns>A task for the lookup.</returns>
    [Fact]
    public async Task ReturnsNullForNotFound()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.UtxosHandler = (_, _) => throw new RpcException(new Status(StatusCode.NotFound, "missing"));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Assert.Null(await provider.GetUtxoByOutRefAsync(TestData.Hash, 0, CancellationToken.None));
    }

    /// <summary>Bad arguments are rejected before any request; empty addresses make no request.</summary>
    /// <param name="badAddress">The malformed address form.</param>
    /// <returns>A task for argument validation.</returns>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("empty-bech32")]
    public async Task RejectsBadArgumentsBeforeCalling(string badAddress)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetUtxoByOutRefAsync("00", 0, CancellationToken.None));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetUtxoByOutRefAsync(new string('z', 64), 0, CancellationToken.None));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetUtxoByOutRefAsync(TestData.Hash, (ulong)uint.MaxValue + 1, CancellationToken.None));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetUtxosAsync([badAddress == "empty-bech32" ? Bech32Util.Encode([], "addr_test") : badAddress], CancellationToken.None));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Assert.Empty(await provider.GetUtxosAsync([], timeout.Token));
        await timeout.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetUtxosAsync([], timeout.Token));
        Assert.Equal(timeout.Token, error.CancellationToken);
        Assert.Equal(0, fake.RequestCount);
    }
}
