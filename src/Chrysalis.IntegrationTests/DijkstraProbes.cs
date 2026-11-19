using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test;
using SAIB.Cbor.Serialization;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.IntegrationTests;

internal static class DijkstraProbes
{
    internal static async Task CaptureForkAsync()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(10));
        CancellationToken ct = deadline.Token;
        await using PeerConnection peer = await PeerConnection.OpenAsync(ct);
        _ = Assert.IsType<MessageIntersectFound>(await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct));
        _ = Assert.IsType<MessageRollBackward>(await peer.ChainSync.NextRequestAsync(ct));
        // Discover an already-final window, not future blocks. Only its four headers are retained.
        List<(ChainSyncHeader Header, ChainPoint Point, byte[] Wire)> window = [];
        while (window.Count < 4 || window[^2].Header.Variant != 7)
        {
            N2NMessageRollForward next = Assert.IsType<N2NMessageRollForward>(await peer.ChainSync.NextRequestAsync(ct));
            ChainSyncHeader header = ChainSyncHeader.Decode(next.Payload.Raw);
            Assert.Contains(header.Variant, new byte[] { 6, 7 });
            if (header.Variant == 6 && window.Count == 2) { window.RemoveAt(0); }
            window.Add((header, ReadPoint(header.HeaderCbor), next.Raw.ToArray()));
        }
        Assert.Equal(new byte[] { 6, 6, 7, 7 }, window.Select(item => item.Header.Variant));
        await peer.ChainSync.DoneAsync(ct);
        await peer.BlockFetch.RequestRangeAsync(Point.Specific(window[0].Point.Slot, window[0].Point.Hash), Point.Specific(window[^1].Point.Slot, window[^1].Point.Hash), ct);
        List<object> files = [];
        List<int> blockEras = [];
        int count = 0;
        await foreach (BlockFetchMessage message in peer.BlockFetch.ReceiveBlockMessagesAsync(ct))
        {
            if (message is not BlockBody block) { continue; }
            // Preserve the node bytes even if a subsequent structural assertion fails.
            object savedBlock = await SaveAsync("block-" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".cbor", block.Body.Value.ToArray(), ct);
            Assert.True(count < window.Count, "The node returned an extra block.");
            (ChainSyncHeader Header, ChainPoint Point, byte[] Wire) expected = window[count];
            (int era, byte[] header) = BlockHeader(block.Body.Value.Span);
            // ouroboros-consensus 808e4005: Cardano/Node.hs:174-180 and Cardano/Block.hs:286-293.
            // Full blocks reserve two Byron tags; the header sum reserves only one.
            Assert.Equal(expected.Header.Variant + 1, era);
            blockEras.Add(era);
            Assert.Equal(expected.Header.HeaderCbor.ToArray(), header);
            if (count > 0)
            {
                Assert.Equal(window[count - 1].Point.BlockNumber + 1, expected.Point.BlockNumber);
                Assert.Equal(window[count - 1].Point.Hash, PreviousHash(header));
            }
            string prefix = expected.Point.BlockNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            files.Add(new
            {
                era,
                height = expected.Point.BlockNumber,
                slot = expected.Point.Slot,
                hash = Convert.ToHexStringLower(expected.Point.Hash),
                previous_hash = Convert.ToHexStringLower(PreviousHash(header)),
                block = savedBlock,
                header = await SaveAsync(prefix + ".header.cbor", header, ct),
                chain_sync = await SaveAsync(prefix + ".chain-sync.cbor", expected.Wire, ct)
            });
            count++;
        }
        Assert.Equal(4, count);
        Assert.Equal<int>([7, 7, 8, 8], blockEras);
        await peer.BlockFetch.DoneAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "fork-manifest.json"), JsonSerializer.Serialize(new
        {
            golden_class = "DJ",
            network_magic = Env.NetworkMagic,
            source = "cardano-node 11.2.1 on public DijkstraNet; N2N ChainSync and BlockFetch",
            image = "sha256:2e20a5932f5f25a98fd871c0caeab663ce104c7d36f8c6a51ea7c588fd2f08dc",
            captured_utc = DateTimeOffset.UtcNow,
            files
        }), ct);
    }

    internal static ChainPoint ReadPoint(ReadOnlyMemory<byte> header)
    {
        CborReader reader = new(header.Span);
        reader.ReadBeginArray(); _ = reader.ReadSize();
        reader.ReadBeginArray(); _ = reader.ReadSize();
        ulong height = reader.ReadUInt64();
        ulong slot = reader.ReadUInt64();
        return new ChainPoint(slot, Blake2Fast.Blake2b.HashData(32, header.Span), height);
    }

    internal static byte[] PreviousHash(ReadOnlySpan<byte> header)
    {
        CborReader reader = new(header);
        reader.ReadBeginArray(); _ = reader.ReadSize();
        reader.ReadBeginArray(); _ = reader.ReadSize();
        _ = reader.ReadUInt64(); _ = reader.ReadUInt64();
        return reader.ReadByteString().ToArray();
    }

    internal static (int Era, byte[] Header) BlockHeader(ReadOnlySpan<byte> block)
    {
        CborReader reader = new(block);
        reader.ReadBeginArray(); _ = reader.ReadSize();
        int era = reader.ReadInt32();
        reader.ReadBeginArray(); _ = reader.ReadSize();
        return (era, reader.ReadDataItem().ToArray());
    }

    private static async Task<object> SaveAsync(string name, byte[] bytes, CancellationToken ct)
    {
        await File.WriteAllBytesAsync(Path.Combine(Env.OutputDirectory, name), bytes, ct);
        return new { path = name, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }

    internal static void AssertNetSpend(ulong input, IEnumerable<ITransactionOutput> outputs)
    {
        ulong netSpend = checked(input - outputs.Aggregate(0UL, (sum, output) => checked(sum + output.Amount().Lovelace())));
        Assert.InRange(netSpend, 1UL, 50_000_000UL);
    }

    internal static void RequireNoAcceptedSubmission()
    {
        using Stream source = typeof(DijkstraProbes).Assembly.GetManifestResourceStream("Golden/Dijkstra/manifest.json")
            ?? throw new InvalidOperationException("Missing Dijkstra Golden manifest; submission is disabled.");
        using JsonDocument golden = JsonDocument.Parse(source);
        if (golden.RootElement.GetProperty("submission").GetProperty("status").GetString() == "accepted")
        {
            throw new InvalidOperationException("DijkstraNet already accepted a submission recorded in Golden; only a reviewed Golden change can re-arm it.");
        }
    }

    internal static async Task SubmitConwayAsync()
    {
        RequireNoAcceptedSubmission();
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        CancellationToken ct = deadline.Token;
        _ = Directory.CreateDirectory(FundedReservation.DijkstraDirectory);
        await using FundedReservation reservation = await FundedReservation.CreateAsync(FundedReservation.DijkstraDirectory, ct);
        await OracleCli.RecordVersionAsync(ct);
        JsonElement tip = await OracleCli.QueryJsonAsync("dijkstra-tip", ct, "tip");
        Assert.Equal("Dijkstra", tip.GetProperty("era").GetString());
        Assert.Equal("100.00", tip.GetProperty("syncProgress").GetString());
        string address = Env.Required(Env.WalletAddress);
        JsonElement before = await OracleCli.QueryJsonAsync("dijkstra-utxos-before", ct, "utxo", "--address", address, "--output-json");
        JsonElement parameters = await OracleCli.QueryJsonAsync("dijkstra-parameters", ct, "protocol-parameters");
        List<ResolvedInput> inputs = FundedIntegrationTests.ReadAdaInputs(before);
        Assert.NotEmpty(inputs);
        ResolvedInput selected = inputs.OrderByDescending(input => input.Output.Amount().Lovelace()).First();
        TxBuilder builder = new(new CliSnapshotProvider(ProtocolOracles.Parameters(parameters), [selected]));
        const ulong transfer = 2_000_000;
        _ = builder.AddInput(selected).PayLovelace(address, transfer).SetChangeAddress(address);
        PostMaryTransaction unsigned = await builder.Complete().WaitAsync(ct);
        _ = Assert.IsType<ConwayTransactionBody>(unsigned.Body);
        ulong fee = unsigned.Body.Fee();
        Assert.InRange(fee, 1UL, 500_000UL);
        AssertNetSpend(selected.Output.Amount().Lovelace(), unsigned.Body.Outputs());
        Assert.All(unsigned.Body.Outputs(), output => Assert.Equal(WalletAddress.FromBech32(address).ToBytes(), output.Address().ToArray()));
        // The PC test process alone opens the original; the shared signer redacts private failures.
        await FundedIntegrationTests.WithWalletKeyAsync(async (key, token) =>
        {
            Assert.Equal(key.GetPublicKey().ToBlake2b224(), WalletAddress.FromBech32(address).ToBytes().AsSpan(1, 28).ToArray());
            ITransaction signed = unsigned.Sign(key);
            byte[] bytes = CborSerializer.Serialize(signed);
            string txid = Convert.ToHexStringLower(Blake2Fast.Blake2b.HashData(32, CborSerializer.Serialize(unsigned.Body)));
            await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "conway.signed.json"), JsonSerializer.Serialize(new { type = "Tx ConwayEra", cborHex = Convert.ToHexStringLower(bytes) }), token);
            _ = await SaveAsync("conway.transaction.cbor", bytes, token);
            await reservation.RecordAsync(new { txid, address, transfer_lovelace = transfer, fee_lovelace = fee, status = "reserved-before-submit" }, token);
            using NodeClient node = await NodeConnections.OpenAsync(token);
            // Conway's wire era is 6, even though the receiving node is already in Dijkstra.
            LocalTxSubmissionMessage answer = await node.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx(6, new CborEncodedValue(bytes))), token);
            Assert.False(answer.Raw.IsEmpty);
            _ = await SaveAsync("conway.node-answer.cbor", answer.Raw.ToArray(), token);
            await reservation.RecordAsync(new { txid, address, transfer_lovelace = transfer, fee_lovelace = fee, status = answer is AcceptTx ? "accepted" : "rejected" }, token);
            _ = Assert.IsType<AcceptTx>(answer);
            for (int attempt = 0; attempt < 45; attempt++)
            {
                JsonElement after = await OracleCli.QueryJsonAsync("dijkstra-utxos-after", token, "utxo", "--address", address, "--output-json");
                if (after.EnumerateObject().Any(output => output.Name.StartsWith(txid + "#", StringComparison.Ordinal))) { return; }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            Assert.Fail("The accepted Conway transaction did not appear at its original address within 90 seconds.");
        }, ct);
    }
}
