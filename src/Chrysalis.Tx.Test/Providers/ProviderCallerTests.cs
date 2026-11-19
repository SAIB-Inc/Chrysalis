using System.Net;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

public class ProviderCallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemplateOnlyReportsMissingWhenOutrefIsAbsent(bool forbidden)
    {
        string change = $"/matches/{BlockfrostTests.Address}?unspent&resolve_hashes";
        string outref = $"/matches/0@{BlockfrostTests.Hash}?unspent&resolve_hashes";
        using StubHttpHandler stub = new(request => request.RequestUri!.PathAndQuery == outref && forbidden
            ? StubHttpHandler.Json("forbidden", HttpStatusCode.Forbidden) : StubHttpHandler.Json("[]"));
        using Kupmios kupmios = KupmiosTests.Create(stub);
        ObservedProvider provider = new(kupmios);
        TransactionTemplate<int> template = TransactionTemplateBuilder.Create<int>(provider)
            .SetChangeAddress(BlockfrostTests.Address)
            .AddInput((options, _) => options.UtxoRef = TransactionInput.Create(new byte[32], 0))
            .Build(eval: false);
        if (forbidden)
        {
            ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(() => template(0));
            Assert.Same(provider.OutrefFailure, error);
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        }
        else
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => template(0));
            Assert.StartsWith("UTxO not found:", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal([change, outref], stub.Requests.Select(r => r.Uri));
    }

    [Fact]
    public async Task CallerCanTreatAnUnusedBlockfrostAddressAsEmpty()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(BlockfrostTests.Absent, HttpStatusCode.NotFound));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        Assert.Empty(await provider.GetUtxosAsync([BlockfrostTests.Address], timeout.Token));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task CompleteSurfacesParameterErrorWithoutReadingUtxos()
    {
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("forbidden", HttpStatusCode.Forbidden));
        using Blockfrost provider = new("CANARY-test-project", url: "https://stub.test/", handler: stub);
        TxBuilder builder = new TxBuilder(provider).SetChangeAddress(BlockfrostTests.Address);
        ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(builder.Complete);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal((HttpMethod.Get, "/epochs/latest/parameters"), Assert.Single(stub.Requests));
    }

    [Fact]
    public async Task DefaultBundleForwardsTokenAndClaimsNoSnapshot()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("[]"));
        using Kupmios kupmios = new("https://kupo.test/", "https://ogmios.test/", NetworkType.Preprod, stub);
        ObservedProvider observed = new(kupmios);
        List<string> addresses = [BlockfrostTests.Address];
        ProviderInputs result = await ((ICardanoDataProvider)observed).GetInputsAsync(addresses, timeout.Token);
        Assert.Equal(new[] { ("parameters", timeout.Token), ("utxos", timeout.Token) }, observed.Reads);
        Assert.Same(addresses, observed.Addresses);
        Assert.Equal("ObservedProvider Preprod", result.Source);
        Assert.Null(result.ParametersPoint);
        Assert.Null(result.UtxosPoint);
        Assert.Null(result.Era);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData(" ", true)]
    public async Task DefaultBundleValidatesAddressesBeforeCancellationAndIo(string? address, bool cancel)
    {
        using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
        if (cancel) { await caller.CancelAsync(); }
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json("forbidden", HttpStatusCode.Forbidden));
        using Blockfrost provider = new("CANARY-test-project", url: "https://blockfrost.test/", handler: stub);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ((ICardanoDataProvider)provider).GetInputsAsync([address!], caller.Token));
        Assert.Empty(stub.Requests);
    }

    private sealed class ObservedProvider(ICardanoDataProvider inner) : ICardanoDataProvider
    {
        public NetworkType NetworkType => inner.NetworkType;
        internal List<(string Method, CancellationToken Token)> Reads { get; } = [];
        internal List<string>? Addresses { get; private set; }
        internal ProviderStatusException? OutrefFailure { get; private set; }

        public Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default)
        {
            Reads.Add(("parameters", cancellationToken));
            return inner.GetParametersAsync(cancellationToken);
        }

        public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
        {
            Addresses = address;
            Reads.Add(("utxos", cancellationToken));
            return inner.GetUtxosAsync(address, cancellationToken);
        }

        public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
        {
            try { return await inner.GetUtxoByOutRefAsync(txHash, index, cancellationToken); }
            catch (ProviderStatusException error)
            {
                OutrefFailure = error;
                throw;
            }
        }

        public Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default) =>
            inner.SubmitTransactionAsync(tx, cancellationToken);

        public Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default) =>
            inner.GetTransactionMetadataAsync(txHash, cancellationToken);
    }
}
