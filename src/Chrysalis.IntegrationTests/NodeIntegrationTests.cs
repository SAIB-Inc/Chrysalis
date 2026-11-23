using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Addresses;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;

namespace Chrysalis.IntegrationTests;

/// <summary>Node-backed mini-protocol tests with independent same-node CLI snapshots.</summary>
public sealed class NodeIntegrationTests
{
    /// <summary>Negotiates a real handshake and verifies the accepted offer and network data.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task HandshakeAcceptsAnOfferedVersionForTheConfiguredNetwork()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        N2CVersionTable offer = Env.CreateN2COffer(Env.NetworkMagic, Environment.GetEnvironmentVariable(Env.ForcedN2CVersion));
        await OracleCli.RecordVersionAsync(deadline.Token);
        _ = await OracleCli.QueryJsonAsync("handshake-node-tip", deadline.Token, "tip");
        using UnixBearer bearer = await UnixBearer.CreateAsync(Env.Required(Env.Socket), deadline.Token);
        using Plexer plexer = new(bearer);
        Task running = plexer.RunAsync(deadline.Token);
        try
        {
            Handshake handshake = new(plexer.SubscribeClient(ProtocolType.Handshake));
            N2CAcceptVersion accepted = await handshake.SendAsync<N2CAcceptVersion>(HandshakeMessages.ProposeVersions(offer), deadline.Token);
            Assert.Contains(accepted.Version.Value, offer.Value.Keys.Select(version => version.Value));
            Assert.Equal(Env.NetworkMagic, accepted.VersionData.NetworkMagic);
            Assert.False(accepted.VersionData.Query);
            await NodeConnections.RecordNegotiationAsync(nameof(HandshakeAcceptsAnOfferedVersionForTheConfiguredNetwork), offer, accepted.Version, accepted.VersionData.NetworkMagic, deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            plexer.Dispose();
            try { await running; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    /// <summary>Acquires and releases state without issuing a query.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task StateAcquireReleaseCompletesOnOneConnection()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        await node.LocalStateQuery.AcquireAsync(null, deadline.Token);
        Assert.True(node.LocalStateQuery.IsAcquired);
        await node.LocalStateQuery.ReleaseAsync(deadline.Token);
        Assert.False(node.LocalStateQuery.IsAcquired);
        await node.LocalStateQuery.AcquireAsync(null, deadline.Token);
        await node.LocalStateQuery.ReleaseAsync(deadline.Token);
        Assert.True(node.IsPlexerHealthy());
    }

    /// <summary>Compares node-to-client chain intersection/rollback with the same node's CLI point.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task ClientChainIntersectionMatchesCliPoint()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        JsonElement tip = await OracleCli.QueryJsonAsync("n2c-chain-tip", deadline.Token, "tip");
        MessageIntersectFound found = Assert.IsType<MessageIntersectFound>(await node.ChainSync.FindIntersectionAsync([ProtocolOracles.Point(tip)], deadline.Token));
        ProtocolOracles.AssertPoint(tip, found.Point);
        MessageRollBackward backward = Assert.IsType<MessageRollBackward>(await node.ChainSync.NextRequestAsync(deadline.Token));
        ProtocolOracles.AssertPoint(tip, backward.Point);
        await node.ChainSync.DoneAsync(deadline.Token);
    }

    /// <summary>Checks the complete tip helper against a stable CLI snapshot.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task LedgerTipHelperMatchesCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        (JsonElement expected, Tip actual) = await OracleCli.AtStableTipAsync("lsq-tip", async (tip, ct) => (tip, await node.LocalStateQuery.GetTipAsync(ct)), deadline.Token);
        ProtocolOracles.AssertPoint(expected, actual.Slot);
    }

    /// <summary>Checks the complete current-era helper, not only its inner model.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task EraHelperMatchesCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        (ulong expected, CurrentEraQueryResponse actual) = await OracleCli.AtStableTipAsync("lsq-era", async (tip, ct) => (ProtocolOracles.Era(tip), await node.LocalStateQuery.GetCurrentEraAsync(ct)), deadline.Token);
        Assert.Equal(expected, actual.CurrentEra);
    }

    /// <summary>Compares the era history with the same node's CLI: the bytes, and the tip's epoch computed from it.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task EraHistoryHelperMatchesCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        (JsonElement tip, string? expected, EraHistoryResponse actual) = await OracleCli.AtStableTipAsync("lsq-era-history", async (before, ct) =>
            (before, (await OracleCli.QueryJsonAsync("lsq-era-history-oracle", ct, "era-history")).GetProperty("cborHex").GetString(), await node.LocalStateQuery.GetEraHistoryAsync(ct)), deadline.Token);
        Assert.Equal(expected, Convert.ToHexStringLower(actual.Raw.Span));
        ulong slot = tip.GetProperty("slot").GetUInt64();
        EraSummary era = Assert.Single(actual.Summaries, summary => summary.Start.Slot <= slot && (summary.End is null || slot < summary.End.Slot));
        Assert.Equal(tip.GetProperty("epoch").GetUInt64(), era.Start.Epoch + ((slot - era.Start.Slot) / era.Parameters.EpochSize));
        Assert.Equal(tip.GetProperty("slotInEpoch").GetUInt64(), (slot - era.Start.Slot) % era.Parameters.EpochSize);
    }

    /// <summary>Checks the system start through the same node's CLI: the slot one minute after it.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task SystemStartHelperMatchesCliSlotNumber()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        SystemStartResponse start = await node.LocalStateQuery.GetSystemStartAsync(deadline.Token);
        EraHistoryResponse history = await node.LocalStateQuery.GetEraHistoryAsync(deadline.Token);
        Assert.Equal(0L, start.Utc.Ticks % TimeSpan.TicksPerSecond);
        BigInteger minute = 60_000_000_000_000;
        EraSummary era = Assert.Single(history.Summaries, summary => summary.Start.TimePicoseconds <= minute && (summary.End is null || minute < summary.End.TimePicoseconds));
        ulong expected = era.Start.Slot + (ulong)((minute - era.Start.TimePicoseconds) / (era.Parameters.SlotLengthMilliseconds * 1_000_000_000UL));
        string actual = await OracleCli.QueryAsync("lsq-system-start-oracle", deadline.Token, "slot-number", start.Utc.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), actual);
    }

    /// <summary>Compares all parameter model fields to the same node's CLI JSON.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task ParameterHelperMatchesCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        (ProtocolParams expected, CurrentProtocolParamsResponse actual) = await OracleCli.AtStableTipAsync("lsq-parameters", async (_, ct) =>
            (ProtocolOracles.Parameters(await OracleCli.QueryJsonAsync("lsq-parameters-oracle", ct, "protocol-parameters")), await node.LocalStateQuery.GetCurrentProtocolParamsAsync(ct)), deadline.Token);
        ProtocolOracles.AssertEquivalent(expected, actual.ProtocolParams);
    }

    /// <summary>Compares the address query's actual inputs, addresses and assets with CLI UTxOs.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.Address])]
    [Trait("Category", "Integration")]
    public async Task UtxoByAddressHelperMatchesCli()
    {
        Env.RequirePreprodOrDevnet();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        await OracleCli.RecordVersionAsync(deadline.Token);
        string address = Env.Required(Env.Address);
        Assert.Equal(0, Address.FromBech32(address).ToBytes()[0] & 15);
        JsonElement initial = await OracleCli.QueryJsonAsync("lsq-address-reference-precondition", deadline.Token, "utxo", "--address", address, "--output-json");
        ProtocolOracles.AssertNonEmptyReferenceUtxos(initial);
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        (JsonElement expected, UtxoByAddressResponse actual) = await OracleCli.AtStableTipAsync("lsq-address", async (_, ct) =>
        {
            JsonElement reference = await OracleCli.QueryJsonAsync("lsq-address-oracle", ct, "utxo", "--address", address, "--output-json");
            ProtocolOracles.AssertNonEmptyReferenceUtxos(reference);
            await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "lsq-address-precondition.json"),
                JsonSerializer.Serialize(new { source = "harness precondition from independent CLI; next step is the Chrysalis query", non_empty = true, reference }), ct);
            return (reference, await node.LocalStateQuery.GetUtxosByAddressAsync([Address.FromBech32(address).ToBytes()], ct));
        }, deadline.Token);
        ProtocolOracles.AssertUtxos(expected, actual);
    }

    /// <summary>Queries the same real nonempty CLI tx-in through both implementations and compares its full output.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.Address])]
    [Trait("Category", "Integration")]
    public async Task InputQueryMatchesSameNonemptyCliInput()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        string address = Env.Required(Env.Address);
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        JsonElement reference = await OracleCli.QueryJsonAsync("lsq-input-address-reference", deadline.Token, "utxo", "--address", address, "--output-json");
        string outref = ProtocolOracles.SelectInputReference(reference);
        TransactionInput input = FundingSelection.ParseInputReference(outref);
        JsonElement expected = await OracleCli.QueryJsonAsync("lsq-real-input-oracle", deadline.Token, "utxo", "--tx-in", outref, "--output-json");
        ProtocolOracles.AssertNonEmptyReferenceUtxos(expected);
        Assert.Equal(outref, Assert.Single(expected.EnumerateObject()).Name);
        ProtocolOracles.AssertUtxos(expected, await node.LocalStateQuery.GetUtxosByTxInAsync([input], deadline.Token));
    }

    /// <summary>Compares the monitor snapshot's size tuple to a stable CLI mempool reading.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task MonitorAcquireAndSizesMatchCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        await OracleCli.RecordVersionAsync(deadline.Token);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
            JsonElement before = await MempoolInfoAsync("monitor-sizes-before", deadline.Token);
            _ = Assert.IsType<Acquired>(await node.LocalTxMonitor.AcquireAsync(deadline.Token));
            ReplyGetSizes sizes = Assert.IsType<ReplyGetSizes>(await node.LocalTxMonitor.GetSizesAsync(deadline.Token));
            JsonElement after = await MempoolInfoAsync("monitor-sizes-after", deadline.Token);
            if (Sizes(before).SequenceEqual(Sizes(after)))
            {
                Assert.Equal(Sizes(before), sizes.Sizes);
                return;
            }
        }
        Assert.Fail("Mempool changed during all three bounded snapshots.");
    }

    /// <summary>Requires release to return without waiting and return the server to idle so reacquisition gets the current snapshot.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task MonitorReleaseThenAcquireCompletes()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        Acquired first = Assert.IsType<Acquired>(await node.LocalTxMonitor.AcquireAsync(deadline.Token));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await node.LocalTxMonitor.ReleaseAsync(deadline.Token);
            Acquired next = Assert.IsType<Acquired>(await node.LocalTxMonitor.AcquireAsync(deadline.Token));
            if (next.Slot == first.Slot)
            {
                return;
            }
            first = next;
        }
        Assert.Fail("Reacquisition after release advanced the snapshot slot on every attempt.");
    }

    /// <summary>Checks a never-submitted id against cardano-cli on the same node.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task MonitorHasTxMatchesCliFalse()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        string id = new('0', 64);
        JsonElement expected = await OracleCli.QueryJsonAsync("monitor-has-oracle", deadline.Token, "tx-mempool", "tx-exists", id);
        Assert.False(expected.GetProperty("exists").GetBoolean());
        _ = await node.LocalTxMonitor.AcquireAsync(deadline.Token);
        Assert.Equal(expected.GetProperty("exists").GetBoolean(), Assert.IsType<ReplyHasTx>(await node.LocalTxMonitor.HasTxAsync(id, deadline.Token)).HasTx);
    }

    /// <summary>Forces 64-bit measures and compares count/byte measurements with CLI, not a fixed capacity.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task MonitorMeasuresMatchCliCountAndBytes()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        await OracleCli.RecordVersionAsync(deadline.Token);
        int oracleReads = 0;
        (JsonElement expected, (ulong Count, Dictionary<string, long[]> Values) actual) = await StableOracleSnapshot.CompareAsync(
            ct => MempoolInfoAsync("monitor-measures-oracle-" + (++oracleReads).ToString(CultureInfo.InvariantCulture), ct),
            (before, after) => Sizes(before).SequenceEqual(Sizes(after)),
            async (reference, ct) =>
            {
                await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "monitor-measures-precondition.json"),
                    JsonSerializer.Serialize(new { source = "harness precondition: two equivalent independent CLI size/count snapshots; next step is Chrysalis Acquire/GetMeasures", oracle_reads_before_subject = oracleReads, reference }), ct);
                using NodeClient node = await NodeConnections.OpenAsync(ct);
                _ = await node.LocalTxMonitor.AcquireAsync(ct);
                ReplyGetMeasures response = Assert.IsType<ReplyGetMeasures>(await node.LocalTxMonitor.GetMeasuresAsync(ct));
                return (Count: response.Slot, Values: response.Measures.ToDictionary(pair => pair.Key, pair => pair.Value.Select(value => checked((long)value)).ToArray(), StringComparer.Ordinal));
            }, deadline.Token);
        Assert.Equal(expected.GetProperty("numberOfTxs").GetUInt64(), actual.Count);
        Assert.Equal([expected.GetProperty("sizeInBytes").GetInt64(), expected.GetProperty("capacityInBytes").GetInt64()], actual.Values["transaction_bytes"]);
        Assert.All(actual.Values.Values, pair => { Assert.Equal(2, pair.Length); Assert.True(pair[0] >= 0 && pair[1] >= pair[0]); });
    }

    /// <summary>Requires a legitimate rejection, corroborated by the same node through CLI, not a CBOR error.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task RejectionIsInspectableAndMatchesCliReason()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        byte[] transaction = Convert.FromHexString("84a300d901028001800200a0f5f6");
        string path = Path.Combine(Env.OutputDirectory, "negative-empty-transaction.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { type = "Tx ConwayEra", description = string.Empty, cborHex = Convert.ToHexStringLower(transaction) }), deadline.Token);
        string cli = await OracleCli.ExecuteAsync("rejection-oracle", ["latest", "transaction", "submit", "--tx-file", path, "--socket-path", Env.Required(Env.Socket), "--testnet-magic", Env.NetworkMagic.ToString(CultureInfo.InvariantCulture)], deadline.Token, expectFailure: true);
        const string capturedReason = "All inputs are spent. Transaction has probably already been included";
        Assert.Contains(capturedReason, cli, StringComparison.Ordinal);
        RejectTx response = Assert.IsType<RejectTx>(await node.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx((int)Era.Conway, new CborEncodedValue(transaction))), deadline.Token));
        string reason = response.Describe();
        Assert.Contains(capturedReason, reason, StringComparison.Ordinal);
        Assert.Contains(capturedReason, cli, StringComparison.Ordinal);
        await using Ouroboros provider = new(Env.Required(Env.Socket), Env.NetworkMagic);
        ProviderException failure = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SubmitTransactionAsync(ITransaction.Read(transaction, Era.Conway), deadline.Token));
        Assert.Equal("Ouroboros SubmitTransactionAsync: transaction rejected", failure.Message);
        Assert.False(failure.IsTransient);
        Assert.Contains(capturedReason, failure.Detail, StringComparison.Ordinal);
    }

    /// <summary>Exercises N2N handshake, chain-sync, block-fetch and keep-alive against one CLI-pinned point.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.Host, Env.Port])]
    [Trait("Category", "Integration")]
    public async Task PeerProtocolsMatchTheSameNodesCliBlockPoint()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        await OracleCli.RecordVersionAsync(deadline.Token);
        JsonElement tip = await OracleCli.QueryJsonAsync("n2n-cli-point", deadline.Token, "tip");
        await using PeerConnection peer = await PeerConnection.OpenAsync(deadline.Token);
        MessageIntersectFound found = Assert.IsType<MessageIntersectFound>(await peer.ChainSync.FindIntersectionAsync([ProtocolOracles.Point(tip)], deadline.Token));
        ProtocolOracles.AssertPoint(tip, found.Point);
        MessageRollBackward backward = Assert.IsType<MessageRollBackward>(await peer.ChainSync.NextRequestAsync(deadline.Token));
        ProtocolOracles.AssertPoint(tip, backward.Point);
        await peer.ChainSync.DoneAsync(deadline.Token);
        await peer.BlockFetch.RequestRangeAsync(ProtocolOracles.Point(tip), ProtocolOracles.Point(tip), deadline.Token);
        int count = 0;
        await foreach (BlockFetchMessage message in peer.BlockFetch.ReceiveBlockMessagesAsync(deadline.Token))
        {
            if (message is not BlockBody blockBody)
            {
                continue;
            }
            BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(blockBody.Body.Value);
            Assert.Equal(tip.GetProperty("slot").GetUInt64(), block.Block.Slot());
            Assert.Equal(tip.GetProperty("hash").GetString(), block.Block.Hash());
            _ = block.Block.TransactionBodies().Select(body => body.Fee()).ToArray();
            count++;
        }
        Assert.Equal(1, count);
        await peer.BlockFetch.DoneAsync(deadline.Token);
        await peer.KeepAlive.KeepAliveRoundtripAsync(deadline.Token);
        await peer.KeepAlive.KeepAliveRoundtripAsync(deadline.Token);
        await peer.KeepAlive.SendDoneAsync(deadline.Token);
    }

    internal static Task<JsonElement> MempoolInfoAsync(string label, CancellationToken cancellationToken) => OracleCli.QueryJsonAsync(label, cancellationToken, "tx-mempool", "info");
    internal static ulong[] Sizes(JsonElement info) => [info.GetProperty("capacityInBytes").GetUInt64(), info.GetProperty("sizeInBytes").GetUInt64(), info.GetProperty("numberOfTxs").GetUInt64()];
}
