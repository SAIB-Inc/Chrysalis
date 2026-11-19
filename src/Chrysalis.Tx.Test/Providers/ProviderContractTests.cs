using System.Net;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class ProviderContractTests
{
    // Operations 0 to 7 use HTTP on Blockfrost; 8 to 14 use it on Kupmios. 15 and 16 are local.
    public static IEnumerable<object[]> Operations => Enumerable.Range(0, 18).Select(i => new object[] { i });

    [Theory]
    [InlineData(true, "\"incomplete")]
    [InlineData(true, "{}")]
    [InlineData(false, "{\"result\":")]
    [InlineData(false, "{\"result\":{}}")]
    public async Task UnusableSuccessfulSubmitHasAnUnknownOutcome(bool blockfrost, string body)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(body));
        using Blockfrost b = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        using Kupmios k = KupmiosTests.Create(stub);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => blockfrost
            ? b.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token)
            : k.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
        Assert.Contains("the transaction may have been accepted", error.Message, StringComparison.Ordinal);
        Assert.False(error.IsTransient);
        Assert.Null(error.InnerException);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task FailuresAndCancellationRespectTheProviderContract(int operation)
    {
        const string canary = "CANARY-xqz62vn8-jt54w4x9";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource canceled = new(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();
        int mode = 0;
        using StubHttpHandler stub = new(_ => mode switch
        {
            0 => StubHttpHandler.Json(canary, HttpStatusCode.TooManyRequests),
            1 => throw new IOException(canary),
            _ => StubHttpHandler.Json("<html>" + canary)
        });
        using Blockfrost blockfrost = new(canary, handler: stub);
        using Kupmios kupmios = new($"https://kupo.test/{canary}/", $"https://ogmios.test/{canary}/", handler: stub);
        async Task Call(CancellationToken ct) => await Invoke(operation, blockfrost, kupmios, ct);
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(canceled.Token));
        Assert.Equal(canceled.Token, failure.CancellationToken);
        Assert.Null(failure.InnerException);
        Assert.Empty(stub.Requests);
        if (operation == 15) { await Call(timeout.Token); Assert.Empty(stub.Requests); return; }
        if (operation == 16)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => kupmios.GetTransactionMetadataAsync(null!, canceled.Token));
            await Assert.ThrowsAsync<NotSupportedException>(() => Call(timeout.Token));
            Assert.Empty(stub.Requests);
            return;
        }
        for (; mode < 3; mode++)
        {
            ProviderException error = await Assert.ThrowsAnyAsync<ProviderException>(() => Call(timeout.Token));
            Assert.Equal(mode < 2, error.IsTransient);
            if (mode == 0) { Assert.Equal(HttpStatusCode.TooManyRequests, Assert.IsType<ProviderStatusException>(error).StatusCode); }
            if (mode == 2) { Assert.Contains("unusable response", error.Message, StringComparison.Ordinal); }
            Assert.Null(error.InnerException);
            ProviderHttpTests.AssertNoCanary(error, canary);
        }
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task ArgumentOrderAndEmptyListsMakeNoRequest()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource canceled = new(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();
        using StubHttpHandler stub = new(_ => throw new InvalidOperationException("Unexpected request."));
        using Blockfrost blockfrost = new("CANARY-contract-key", handler: stub);
        using Kupmios kupmios = KupmiosTests.Create(stub);
        await Assert.ThrowsAsync<ArgumentNullException>(() => blockfrost.GetUtxosAsync((List<string>)null!, canceled.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => kupmios.GetUtxosAsync(null!, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockfrost.GetUtxosAsync([], canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kupmios.GetUtxosAsync([], canceled.Token));
        Assert.Empty(await blockfrost.GetUtxosAsync([], timeout.Token));
        Assert.Empty(await kupmios.GetUtxosAsync([], timeout.Token));
        Assert.Empty(await kupmios.GetUtxosByPaymentKeysAsync([], timeout.Token));
        Assert.Empty(await kupmios.GetUtxosByOutRefsAsync(new List<TransactionInput>(), timeout.Token));
        Assert.Empty(stub.Requests);
    }

    private static Task Invoke(int operation, Blockfrost b, Kupmios k, CancellationToken cancellationToken) => operation switch
    {
        0 => b.GetUtxosAsync(BlockfrostTests.Address, cancellationToken),
        1 => b.GetUtxosAsync([BlockfrostTests.Address], cancellationToken),
        2 => b.GetParametersAsync(cancellationToken),
        3 => b.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 0, cancellationToken),
        4 => b.GetTransactionMetadataAsync(BlockfrostTests.Hash, cancellationToken),
        5 => b.SubmitTransactionAsync(BlockfrostTests.Transaction, cancellationToken),
        6 => b.EvaluateTransactionAsync(BlockfrostTests.Transaction, cancellationToken),
        7 => b.GetScript(BlockfrostTests.Hash, cancellationToken),
        8 => k.GetUtxosAsync([BlockfrostTests.Address], cancellationToken),
        9 => k.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 0, cancellationToken),
        10 => k.GetUtxoByOutRefAsync(TransactionInput.Create(new byte[32], 0), cancellationToken),
        11 => k.GetUtxosByOutRefsAsync([(BlockfrostTests.Hash, 0)], cancellationToken),
        12 => k.GetUtxosByOutRefsAsync([TransactionInput.Create(new byte[32], 0)], cancellationToken),
        13 => k.GetUtxosByPaymentKeyAsync(BlockfrostTests.Hash, cancellationToken),
        14 => k.GetUtxosByPaymentKeysAsync([BlockfrostTests.Hash], cancellationToken),
        15 => k.GetParametersAsync(cancellationToken),
        16 => k.GetTransactionMetadataAsync(BlockfrostTests.Hash, cancellationToken),
        17 => k.SubmitTransactionAsync(BlockfrostTests.Transaction, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
