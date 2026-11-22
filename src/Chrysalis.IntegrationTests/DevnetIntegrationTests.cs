using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;
using LsqAcquired = Chrysalis.Network.Cbor.LocalStateQuery.Messages.Acquired;
using LsqRelease = Chrysalis.Network.Cbor.LocalStateQuery.Messages.Release;
using MonitorAcquired = Chrysalis.Network.Cbor.LocalTxMonitor.Acquired;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.IntegrationTests;

/// <summary>Devnet smoke checks for Ogmios and Kupo, plus the owed held-mempool and acquire-failure tests.</summary>
public sealed class DevnetIntegrationTests
{
    private const string EmptyTransactionHex = "84a300d901028001800200a0f5f6";

    /// <summary>Verifies that Ogmios answers tip, protocol-parameters and negative submitTransaction calls as Kupmios expects.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.OgmiosUrl])]
    [Trait("Category", "Integration")]
    public async Task OgmiosAnswersTheCallsKupmiosMakes()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(1));
        await OracleCli.RecordVersionAsync(deadline.Token);
        using HttpClient http = new();
        Uri endpoint = new(Env.Required(Env.OgmiosUrl));

        (JsonElement cliTip, JsonElement ogmiosTip) = await OracleCli.AtStableTipAsync("ogmios-tip", async (tip, ct) =>
        {
            (HttpStatusCode status, JsonElement body) = await PostOgmiosAsync(http, endpoint, "queryNetwork/tip", null, 1, ct);
            Assert.Equal(HttpStatusCode.OK, status);
            return (tip, body.GetProperty("result"));
        }, deadline.Token);
        Assert.Equal(cliTip.GetProperty("slot").GetUInt64(), ogmiosTip.GetProperty("slot").GetUInt64());
        Assert.Equal(cliTip.GetProperty("hash").GetString(), ogmiosTip.GetProperty("id").GetString());

        JsonElement cliParameters = await OracleCli.QueryJsonAsync("ogmios-parameters-oracle", deadline.Token, "protocol-parameters");
        (HttpStatusCode paramsStatus, JsonElement paramsBody) = await PostOgmiosAsync(http, endpoint, "queryLedgerState/protocolParameters", null, 2, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, paramsStatus);
        Assert.Equal(
            cliParameters.GetProperty("txFeePerByte").GetUInt64(),
            paramsBody.GetProperty("result").GetProperty("minFeeCoefficient").GetUInt64());

        (HttpStatusCode submitStatus, JsonElement submitBody) = await PostOgmiosAsync(
            http, endpoint, "submitTransaction", new { transaction = new { cbor = EmptyTransactionHex } }, 3, deadline.Token);
        Assert.Equal(HttpStatusCode.BadRequest, submitStatus);
        Assert.True(submitBody.TryGetProperty("error", out _), "Ogmios negative submitTransaction did not return an error object.");
        Assert.False(submitBody.TryGetProperty("result", out _), "Ogmios negative submitTransaction unexpectedly returned a result object.");
    }

    /// <summary>Verifies that Kupo reaches the tip and matches CLI UTxOs by address, payment credential and output reference.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.KupoUrl, Env.Address])]
    [Trait("Category", "Integration")]
    public async Task KupoAnswersTheCallsKupmiosMakes()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(1));
        await OracleCli.RecordVersionAsync(deadline.Token);
        string address = Env.Required(Env.Address);
        string paymentKeyHash = Convert.ToHexStringLower(WalletAddress.FromBech32(address).ToBytes().AsSpan(1, 28));
        using HttpClient http = new() { BaseAddress = new Uri(Env.Required(Env.KupoUrl).TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        (Dictionary<string, ulong> cliSet, Dictionary<string, ulong> byAddress, Dictionary<string, ulong> byCredential, Dictionary<string, ulong> byOutref, string firstOutref) =
            await OracleCli.AtStableTipAsync("kupo-utxo", async (tip, ct) =>
            {
                ulong tipSlot = tip.GetProperty("slot").GetUInt64();
                bool caughtUp = false;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    using HttpResponseMessage healthResponse = await http.GetAsync(new Uri("health", UriKind.Relative), ct);
                    _ = healthResponse.EnsureSuccessStatusCode();
                    using JsonDocument health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync(ct));
                    if (health.RootElement.TryGetProperty("most_recent_checkpoint", out JsonElement checkpoint)
                        && checkpoint.ValueKind == JsonValueKind.Number
                        && checkpoint.GetUInt64() >= tipSlot)
                    {
                        caughtUp = true;
                        break;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
                if (!caughtUp)
                {
                    Assert.Fail("Kupo did not reach the CLI tip slot");
                }

                JsonElement cliUtxos = await OracleCli.QueryJsonAsync("kupo-utxo-oracle", ct, "utxo", "--address", address, "--output-json");
                string selected = ProtocolOracles.SelectInputReference(cliUtxos);
                Dictionary<string, ulong> expected = cliUtxos.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => property.Value.GetProperty("value").GetProperty("lovelace").GetUInt64(),
                    StringComparer.OrdinalIgnoreCase);

                Dictionary<string, ulong> addressMatches = await FetchKupoMatchesAsync(http, Uri.EscapeDataString(address), ct);
                Dictionary<string, ulong> credentialMatches = await FetchKupoMatchesAsync(http, paymentKeyHash + "/*", ct);
                string[] parts = selected.Split('#');
                Dictionary<string, ulong> outrefMatches = await FetchKupoMatchesAsync(http, $"{parts[1]}@{parts[0]}", ct);
                return (expected, addressMatches, credentialMatches, outrefMatches, selected);
            }, deadline.Token);

        Assert.NotEmpty(cliSet);
        AssertUtxoAmountsEqual(cliSet, byAddress);
        AssertUtxoAmountsEqual(cliSet, byCredential);
        KeyValuePair<string, ulong> single = Assert.Single(byOutref);
        Assert.Equal(firstOutref, single.Key, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(cliSet[firstOutref], single.Value);
    }

    /// <summary>Submits a devnet self-transfer and verifies LocalTxMonitor has-tx, next-tx bytes, sizes and measures while held.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.WalletFile, Env.FundedOptIn, Env.CollateralTxIn])]
    [Trait("Category", "Integration")]
    public async Task MonitorHoldsASubmittedTransaction()
    {
        Env.RequireDevnet();
        Env.RequireFundedOptIn();
        TransactionInput collateral = FundingSelection.ParseInputReference(Env.Required(Env.CollateralTxIn));
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(4));
        await OracleCli.RecordVersionAsync(deadline.Token);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            FundedIntegrationTests.SelfTransfer? built = null;
            await FundedIntegrationTests.WithWalletKeyAsync(
                async (key, ct) => built = await FundedIntegrationTests.BuildSelfTransferAsync(key, collateral, 2_000_000, ct),
                deadline.Token);
            FundedIntegrationTests.SelfTransfer transfer = built ?? throw new InvalidOperationException("Self-transfer was not built.");

            using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
            Stopwatch window = Stopwatch.StartNew();
            _ = Assert.IsType<AcceptTx>(await node.LocalTxSubmit.SubmitTxAsync(
                new SubmitTx(0, new EraTx((int)Era.Conway, new CborEncodedValue(transfer.SignedBytes))),
                deadline.Token));

            JsonElement existsBefore = await OracleCli.QueryJsonAsync($"monitor-held-exists-before-{attempt}", deadline.Token, "tx-mempool", "tx-exists", transfer.TxId);
            JsonElement infoBefore = await NodeIntegrationTests.MempoolInfoAsync($"monitor-held-info-before-{attempt}", deadline.Token);

            _ = Assert.IsType<MonitorAcquired>(await node.LocalTxMonitor.AcquireAsync(deadline.Token));
            ReplyHasTx hasTx = Assert.IsType<ReplyHasTx>(await node.LocalTxMonitor.HasTxAsync(transfer.TxId, deadline.Token));
            ReplyGetSizes sizes = Assert.IsType<ReplyGetSizes>(await node.LocalTxMonitor.GetSizesAsync(deadline.Token));
            ReplyGetMeasures measures = Assert.IsType<ReplyGetMeasures>(await node.LocalTxMonitor.GetMeasuresAsync(deadline.Token));

            bool foundSigned = false;
            for (int step = 0; step < 8; step++)
            {
                ReplyNextTx next = Assert.IsType<ReplyNextTx>(await node.LocalTxMonitor.NextTxAsync(deadline.Token));
                if (next.EraTx is null)
                {
                    break;
                }
                if (next.EraTx.Tx.Value.Span.SequenceEqual(transfer.SignedBytes))
                {
                    foundSigned = true;
                    break;
                }
            }

            await node.LocalTxMonitor.ReleaseAsync(deadline.Token);
            JsonElement existsAfter = await OracleCli.QueryJsonAsync($"monitor-held-exists-after-{attempt}", deadline.Token, "tx-mempool", "tx-exists", transfer.TxId);
            JsonElement infoAfter = await NodeIntegrationTests.MempoolInfoAsync($"monitor-held-info-after-{attempt}", deadline.Token);
            window.Stop();

            string outref = transfer.TxId + "#" + transfer.Index.ToString(CultureInfo.InvariantCulture);
            await FundedIntegrationTests.WaitForOutputAsync(outref, 2_000_000, deadline.Token);

            ulong[] beforeSizes = NodeIntegrationTests.Sizes(infoBefore);
            if (existsBefore.GetProperty("exists").GetBoolean()
                && existsAfter.GetProperty("exists").GetBoolean()
                && beforeSizes.SequenceEqual(NodeIntegrationTests.Sizes(infoAfter)))
            {
                Assert.True(hasTx.HasTx, "LocalTxMonitor.HasTxAsync returned false for a held transaction.");
                Assert.True(foundSigned, "LocalTxMonitor.NextTxAsync did not return the submitted transaction bytes.");
                Assert.Equal(beforeSizes, sizes.Sizes);
                Assert.True(beforeSizes[2] >= 1UL, "Expected at least one transaction in the held mempool.");
                Assert.Equal(beforeSizes[2], measures.Slot);
                Dictionary<string, long[]> actualMeasures = measures.Measures.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Select(value => checked((long)value)).ToArray(),
                    StringComparer.Ordinal);
                Assert.Equal([checked((long)beforeSizes[1]), checked((long)beforeSizes[0])], actualMeasures["transaction_bytes"]);
                await File.WriteAllTextAsync(
                    Path.Combine(Env.OutputDirectory, "monitor-held-observation.json"),
                    JsonSerializer.Serialize(new { attempts = attempt, window_ms = window.ElapsedMilliseconds }),
                    deadline.Token);
                return;
            }
        }

        Assert.Fail("A block took each held transaction before the snapshot.");
    }

    /// <summary>Captures the node's raw LocalStateQuery MsgFailure bytes for PointNotOnChain and PointTooOld and checks AcquireAsync.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task AcquireFailuresAreTheNodesReplies()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        await OracleCli.RecordVersionAsync(deadline.Token);
        JsonElement tip = await OracleCli.QueryJsonAsync("acquire-failure-tip", deadline.Token, "tip");
        Point missingPoint = Point.Specific(tip.GetProperty("slot").GetUInt64(), new byte[32]);
        byte[] notOnChainRequest = CborSerializer.Serialize(AcquireTypes.SpecificPoint(missingPoint));
        byte[] tooOldRequest = CborSerializer.Serialize(AcquireTypes.SpecificPoint(Point.Origin));
        byte[] releaseRequest = CborSerializer.Serialize(new LsqRelease(5));
        byte[] expectedNotOnChain = Convert.FromHexString("820201");
        byte[] expectedTooOld = Convert.FromHexString("820200");

        using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
        {
            using UnixBearer bearer = await UnixBearer.CreateAsync(Env.Required(Env.Socket), stop.Token);
            using Plexer plexer = new(bearer);
            Handshake handshake = new(plexer.SubscribeClient(ProtocolType.Handshake));
            AgentChannel lsq = plexer.SubscribeClient(ProtocolType.LocalStateQuery);
            Task running = plexer.RunAsync(stop.Token);
            try
            {
                N2CVersionTable offer = Env.CreateN2COffer(Env.NetworkMagic, Environment.GetEnvironmentVariable(Env.ForcedN2CVersion));
                _ = Assert.IsType<N2CAcceptVersion>(await handshake.SendAsync(HandshakeMessages.ProposeVersions(offer), stop.Token));

                await lsq.EnqueueChunkAsync(new ReadOnlySequence<byte>(notOnChainRequest), stop.Token);
                byte[] notOnChainReply = (await lsq.ReadSegmentAsync(stop.Token)).ToArray();
                Assert.True(notOnChainReply.AsSpan().SequenceEqual(expectedNotOnChain), "Expected CBOR 820201 for AcquireFailurePointNotOnChain.");
                Failure notOnChainDecoded = Assert.IsType<Failure>(CborSerializer.Deserialize<LocalStateQueryMessage>(notOnChainReply));
                _ = Assert.IsType<AcquireFailurePointNotOnChain>(notOnChainDecoded.Reason);

                byte[]? tooOldReply = null;
                while (tooOldReply is null)
                {
                    await lsq.EnqueueChunkAsync(new ReadOnlySequence<byte>(tooOldRequest), stop.Token);
                    byte[] reply = (await lsq.ReadSegmentAsync(stop.Token)).ToArray();
                    LocalStateQueryMessage decoded = CborSerializer.Deserialize<LocalStateQueryMessage>(reply);
                    if (decoded is LsqAcquired)
                    {
                        await lsq.EnqueueChunkAsync(new ReadOnlySequence<byte>(releaseRequest), stop.Token);
                        await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
                        continue;
                    }
                    Assert.True(reply.AsSpan().SequenceEqual(expectedTooOld), "Expected CBOR 820200 for AcquireFailurePointTooOld.");
                    Failure tooOldDecoded = Assert.IsType<Failure>(decoded);
                    _ = Assert.IsType<AcquireFailurePointTooOld>(tooOldDecoded.Reason);
                    tooOldReply = reply;
                }

                string directory = Env.OutputDirectory;
                await File.WriteAllBytesAsync(Path.Combine(directory, "acquire-failure-not-on-chain.cbor"), notOnChainReply, stop.Token);
                await File.WriteAllBytesAsync(Path.Combine(directory, "acquire-failure-too-old.cbor"), tooOldReply, stop.Token);
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "acquire-failure.json"),
                    JsonSerializer.Serialize(new
                    {
                        source = "node reply, not an SDK observation",
                        node = Env.Required(Env.NodeVersion),
                        networkMagic = Env.NetworkMagic,
                        notOnChain = new
                        {
                            file = "acquire-failure-not-on-chain.cbor",
                            requestHex = Convert.ToHexStringLower(notOnChainRequest),
                            sha256 = Convert.ToHexStringLower(SHA256.HashData(notOnChainReply))
                        },
                        tooOld = new
                        {
                            file = "acquire-failure-too-old.cbor",
                            requestHex = Convert.ToHexStringLower(tooOldRequest),
                            sha256 = Convert.ToHexStringLower(SHA256.HashData(tooOldReply))
                        }
                    }),
                    stop.Token);
            }
            finally
            {
                await stop.CancelAsync();
                plexer.Dispose();
                try { await running; }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }

        JsonElement freshTip = await OracleCli.QueryJsonAsync("acquire-failure-fresh-tip", deadline.Token, "tip");
        Point freshMissingPoint = Point.Specific(freshTip.GetProperty("slot").GetUInt64(), new byte[32]);
        using NodeClient node = await NodeConnections.OpenAsync(deadline.Token);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => node.LocalStateQuery.AcquireAsync(freshMissingPoint, deadline.Token));
        Assert.Contains(nameof(AcquireFailurePointNotOnChain), failure.Message, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostOgmiosAsync(
        HttpClient http, Uri endpoint, string method, object? parameters, int id, CancellationToken cancellationToken)
    {
        string json = parameters is null
            ? JsonSerializer.Serialize(new { jsonrpc = "2.0", method, id })
            : JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters, id });
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(endpoint, content, cancellationToken);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<Dictionary<string, ulong>> FetchKupoMatchesAsync(HttpClient http, string pattern, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri($"matches/{pattern}?unspent&resolve_hashes", UriKind.Relative), cancellationToken);
        _ = response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("transaction_id").GetString()! + "#" + item.GetProperty("output_index").GetUInt64().ToString(CultureInfo.InvariantCulture),
            item => item.GetProperty("value").GetProperty("coins").GetUInt64(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertUtxoAmountsEqual(Dictionary<string, ulong> expected, Dictionary<string, ulong> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (KeyValuePair<string, ulong> pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out ulong coins), "Missing Kupo UTxO: " + pair.Key);
            Assert.Equal(pair.Value, coins);
        }
    }
}
