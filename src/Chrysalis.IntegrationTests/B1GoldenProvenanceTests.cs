using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline attribution contracts for each independently pinned CLI producer.</summary>
public sealed class B1GoldenProvenanceTests
{
    /// <summary>Every node's license and notice paths are Golden-relative and name retained bytes with verified digests.</summary>
    /// <param name="nodeVersion">The node associated with this producer pin.</param>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("11.1.3")]
    [InlineData("11.2.1")]
    public void PinnedCliAttributionUsesExistingGoldenRelativePaths(string nodeVersion)
    {
        using JsonDocument sources = JsonDocument.Parse(Read("SOURCES.json"));
        JsonElement pin = sources.RootElement.GetProperty("cardano_cli_by_node").GetProperty(nodeVersion);
        Assert.Equal(pin.GetProperty("retained_license_sha256").GetString(), Hash(Read(pin.GetProperty("license_path").GetString()!)));
        Assert.Equal(pin.GetProperty("retained_notice_sha256").GetString(), Hash(Read(pin.GetProperty("notice_path").GetString()!)));
    }

    /// <summary>Replays the independent public fork window and the node's accepted Conway transaction offline.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void DijkstraGoldensPinBothSidesOfTheForkAndTheAcceptedConwayTransaction()
    {
        using JsonDocument sources = JsonDocument.Parse(Read("SOURCES.json"));
        JsonElement pin = sources.RootElement.GetProperty("dijkstranet");
        Assert.Equal("Apache-2.0", pin.GetProperty("license").GetString());
        Assert.Equal(pin.GetProperty("retained_license_sha256").GetString(), Hash(Read(pin.GetProperty("license_path").GetString()!)));
        Assert.Equal(pin.GetProperty("retained_notice_sha256").GetString(), Hash(Read(pin.GetProperty("notice_path").GetString()!)));
        byte[] manifest = Read(pin.GetProperty("manifest").GetString()!);
        Assert.Equal(pin.GetProperty("manifest_sha256").GetString(), Hash(manifest));
        using JsonDocument document = JsonDocument.Parse(manifest);
        JsonElement root = document.RootElement;
        static byte[] Artifact(JsonElement artifact)
        {
            byte[] bytes = Read("Dijkstra/" + artifact.GetProperty("path").GetString());
            Assert.Equal(artifact.GetProperty("sha256").GetString(), Hash(bytes));
            return bytes;
        }
        foreach (JsonElement artifact in root.GetProperty("artifacts").EnumerateArray()) { _ = Artifact(artifact); }
        Assert.Equal(6, root.GetProperty("network_magic").GetInt32());
        using JsonDocument configuration = JsonDocument.Parse(Read("Dijkstra/config.json"));
        IEnumerable<(string Name, string? Hash)> genesisHashes = configuration.RootElement.GetProperty("Configuration").EnumerateObject()
            .SelectMany(section => section.Value.EnumerateObject()).Where(field => field.Name.EndsWith("GenesisHash", StringComparison.Ordinal))
            .Select(field => (field.Name, field.Value.GetString()));
        Assert.Equal(genesisHashes.OrderBy(field => field.Name, StringComparer.Ordinal), root.GetProperty("genesis_hashes").EnumerateObject()
            .Select(field => (field.Name, field.Value.GetString())).OrderBy(field => field.Name, StringComparer.Ordinal));
        JsonElement[] window = [.. root.GetProperty("files").EnumerateArray()];
        Assert.Equal([7, 7, 8, 8], window.Select(row => row.GetProperty("era").GetInt32()));
        byte[]? previous = null;
        ulong? height = null;
        foreach (JsonElement row in window)
        {
            byte[] block = Artifact(row.GetProperty("block"));
            (int era, byte[] header) = DijkstraProbes.BlockHeader(block);
            Assert.Equal(row.GetProperty("era").GetInt32(), era);
            byte[] wire = Artifact(row.GetProperty("chain_sync"));
            N2NMessageRollForward forward = Assert.IsType<N2NMessageRollForward>(CborSerializer.Deserialize<MessageNextResponse>(wire));
            ChainSyncHeader peerHeader = ChainSyncHeader.Decode(forward.Payload.Raw);
            Assert.Equal(era - 1, peerHeader.Variant);
            Assert.Equal(peerHeader.HeaderCbor.ToArray(), header);
            Assert.Equal(Artifact(row.GetProperty("header")), header);
            ChainPoint point = DijkstraProbes.ReadPoint(header);
            Assert.Equal(row.GetProperty("hash").GetString(), Convert.ToHexStringLower(point.Hash));
            Assert.Equal(row.GetProperty("slot").GetUInt64(), point.Slot);
            Assert.Equal(row.GetProperty("height").GetUInt64(), point.BlockNumber);
            Assert.Equal(row.GetProperty("previous_hash").GetString(), Convert.ToHexStringLower(DijkstraProbes.PreviousHash(header)));
            if (previous is not null)
            {
                Assert.Equal(previous, DijkstraProbes.PreviousHash(header));
                Assert.Equal(height + 1, point.BlockNumber);
            }
            previous = point.Hash;
            height = point.BlockNumber;
        }
        foreach (JsonProperty attribute in pin.EnumerateObject().Where(field => field.Name is not ("manifest" or "manifest_sha256" or "golden_class" or "notice")))
        {
            Assert.Equal(attribute.Value.GetRawText(), root.GetProperty("sources").GetProperty(attribute.Name).GetRawText());
        }
        JsonElement submission = root.GetProperty("submission");
        _ = Assert.IsType<AcceptTx>(CborSerializer.Deserialize<LocalTxSubmissionMessage>(Read("Dijkstra/conway.node-answer.cbor")));
        PostMaryTransaction transaction = CborSerializer.Deserialize<PostMaryTransaction>(Read("Dijkstra/conway.transaction.cbor"));
        Assert.Equal(submission.GetProperty("fee_lovelace").GetUInt64(), transaction.Body.Fee());
        string txid = Convert.ToHexStringLower(Blake2Fast.Blake2b.HashData(32, CborSerializer.Serialize(transaction.Body)));
        Assert.Equal(submission.GetProperty("txid").GetString(), txid);
        using JsonDocument before = JsonDocument.Parse(Read("Dijkstra/utxos-before.json"));
        using JsonDocument after = JsonDocument.Parse(Read("Dijkstra/utxos-after.json"));
        string? address = Assert.Single(before.RootElement.EnumerateObject()).Value.GetProperty("address").GetString();
        ITransactionOutput[] outputs = [.. transaction.Body.Outputs()];
        Assert.Equal(2, outputs.Length);
        Dictionary<TransactionInput, ITransactionOutput> resolved = [];
        for (int index = 0; index < outputs.Length; index++)
        {
            resolved.Add(TransactionInput.Create(Convert.FromHexString(txid), checked((ulong)index)), outputs[index]);
        }
        ProtocolOracles.AssertUtxos(after.RootElement, new UtxoByAddressResponse(resolved));
        Assert.All(after.RootElement.EnumerateObject(), output => Assert.Equal(address, output.Value.GetProperty("address").GetString()));
        Assert.Contains(outputs, output => output.Amount().Lovelace() == submission.GetProperty("transfer_lovelace").GetUInt64());
        Assert.Equal(address, submission.GetProperty("address").GetString());
        static ulong Total(JsonElement utxos) => utxos.EnumerateObject().Aggregate(0UL, (sum, output) => checked(sum + output.Value.GetProperty("value").GetProperty("lovelace").GetUInt64()));
        Assert.Equal(transaction.Body.Fee(), checked(Total(before.RootElement) - Total(after.RootElement)));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Read(string path)
    {
        Assert.False(Path.IsPathRooted(path));
        using Stream? resource = typeof(B1GoldenProvenanceTests).Assembly.GetManifestResourceStream("Golden/" + path);
        Assert.NotNull(resource);
        using MemoryStream bytes = new();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }
}
