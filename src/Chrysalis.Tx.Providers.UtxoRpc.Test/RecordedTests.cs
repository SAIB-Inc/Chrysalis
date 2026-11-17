using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Tx.Extensions;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Grpc.Core;
using Utxorpc.V1Beta.Query;
using Utxorpc.V1Beta.Submit;
using Xunit;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Replays independently recorded Dolos answers through the public provider.</summary>
public sealed class RecordedTests
{
    private const string Folder = "Golden/dolos-1.6.3-preprod/";

    /// <summary>The recording has exactly the named server gaps against the unchanged node capture.</summary>
    /// <returns>A task for all protocol-parameter fields.</returns>
    [Fact]
    public async Task ParametersMatchTheNodeExceptForNamedDolosGaps()
    {
        ReadParamsResponse recorded = await LoadAsync("read-params.pb", ReadParamsResponse.Parser);
        string json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden/cardano-node-11.1.3-preprod/protocol-parameters.json"));
        JsonObject expected = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        foreach ((string key, string? part, ulong? value) in DolosGaps.Values)
        {
            if (value is null) { Assert.True(expected.Remove(key)); }
            else if (part is null) { expected[key] = value.Value; }
            else { expected[key]![part] = value.Value; }
        }
        // JsonNode retains untouched JSON numbers as JsonElements, including 7.21e-05.
        using JsonDocument original = JsonDocument.Parse(json);
        using JsonDocument adjusted = JsonDocument.Parse(expected.ToJsonString());
        foreach (JsonProperty price in original.RootElement.GetProperty("executionUnitPrices").EnumerateObject())
        {
            Assert.Equal(price.Value.GetRawText(), adjusted.RootElement.GetProperty("executionUnitPrices").GetProperty(price.Name).GetRawText());
        }
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = (_, _) => Task.FromResult(recorded);
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        ProtocolParams actual = await provider.GetParametersAsync();
        LedgerParameters.Equal(adjusted.RootElement, actual);
        // pallas-utxorpc v1.4.0 shared.rs:986-1096 omits field 9 (set from v1.5.0-rc.1).
        Assert.Equal(0U, recorded.Values.Cardano.PoolRetirementEpochBound);
        Assert.Null(actual.MaximumEpoch);
    }

    /// <summary>Search requests and every output agree with the independent parsed data.</summary>
    /// <returns>A task for the recorded address and output shapes.</returns>
    [Fact]
    public async Task SearchMatchesParsedOutputsAndRequestBytes()
    {
        SearchUtxosResponse recorded = await LoadAsync("search-utxos.pb", SearchUtxosResponse.Parser);
        string address = Request("search-utxos.pb")["SearchUtxos exact_address=".Length..];
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.SearchHandler = (request, _) =>
        {
            for (int i = 0; i < recorded.Items.Count; i++)
            {
                Assert.Equal(recorded.Items[i].Cardano.Address, request.Predicate.Match.Cardano.Address.ExactAddress);
            }
            return Task.FromResult(recorded);
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        List<ResolvedInput> actual = await provider.GetUtxosAsync([address]);
        Assert.Equal(recorded.Items.Count, actual.Count);
        for (int i = 0; i < actual.Count; i++) { Equal(recorded.Items[i], actual[i]); }
    }

    /// <summary>Every recorded reference is requested in its original hash and index form.</summary>
    /// <param name="file">The primary outputs or the public-contract hash-datum sample.</param>
    /// <returns>A task for all recorded reference lookups.</returns>
    [Theory]
    [InlineData("read-utxos.pb")]
    [InlineData("read-hash-utxos.pb")]
    public async Task ReferenceLookupsMatchParsedOutputsAndRequestBytes(string file)
    {
        ReadUtxosResponse recorded = await LoadAsync(file, ReadUtxosResponse.Parser);
        string[] references = Request(file)["ReadUtxos ".Length..].Split(',');
        if (file == "read-hash-utxos.pb")
        {
            Assert.InRange(recorded.Items.Count, 5, 10);
            Assert.Contains(recorded.Items, item => item.Cardano.Assets.Count > 0);
            foreach (AnyUtxoData item in recorded.Items)
            {
                Assert.Equal(32, item.Cardano.Datum.Hash.Length);
                Assert.True(item.Cardano.Datum.OriginalCbor.IsEmpty);
                Assert.Null(item.Cardano.Datum.Payload);
            }
        }
        Assert.Equal(recorded.Items.Count, references.Length);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        foreach (string reference in references)
        {
            string[] parts = reference.Split('#');
            uint index = uint.Parse(parts[1], CultureInfo.InvariantCulture);
            AnyUtxoData item = Assert.Single(recorded.Items, item => Convert.ToHexStringLower(item.TxoRef.Hash.Span) == parts[0] && item.TxoRef.Index == index);
            fake.UtxosHandler = (request, _) =>
            {
                TxoRef key = Assert.Single(request.Keys);
                Assert.Equal(item.TxoRef, key);
                return Task.FromResult(recorded);
            };
            Equal(item, Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(parts[0], index)));
        }
    }

    /// <summary>Metadata agrees recursively with Pallas's parsed metadata.</summary>
    /// <returns>A task for the recorded transaction.</returns>
    [Fact]
    public async Task MetadataMatchesTheParsedTransaction()
    {
        ReadTxResponse recorded = await LoadAsync("read-tx.pb", ReadTxResponse.Parser);
        string hash = Request("read-tx.pb")["ReadTx ".Length..];
        Assert.Equal(hash, Convert.ToHexStringLower(recorded.Tx.Cardano.Hash.Span));
        Assert.NotEmpty(recorded.Tx.Cardano.Auxiliary.Metadata);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.TxHandler = (request, _) =>
        {
            Assert.Equal(recorded.Tx.Cardano.Hash, request.Hash);
            return Task.FromResult(recorded);
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        LedgerMetadata? actual = await provider.GetTransactionMetadataAsync(hash);
        ParsedMetadata.Equal(recorded.Tx.Cardano.Auxiliary.Metadata, actual);
    }

    /// <summary>The native reference script hashes to the value independently reported by Blockfrost.</summary>
    /// <returns>A task for the immutable reference-script output.</returns>
    [Fact]
    public async Task ReferenceScriptHashMatchesTheIndependentEndpoint()
    {
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, Folder, "refscript-hash.tsv"));
        Assert.Equal(2, lines.Length);
        string[] expected = lines[1].Split('\t');
        Assert.Equal(3, expected.Length);
        Assert.Equal(Request("refscript-hash.tsv"), expected[1]);
        string[] request = expected[1].Split(' ');
        string hash = request[1].Split('/')[2];
        uint index = uint.Parse(request[2]["output_index=".Length..], CultureInfo.InvariantCulture);
        ReadUtxosResponse recorded = await LoadAsync("read-utxos.pb", ReadUtxosResponse.Parser);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.UtxosHandler = (_, _) => Task.FromResult(recorded);
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        ResolvedInput value = Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(hash, index));
        ReadOnlyMemory<byte> bytes = Assert.IsType<ReadOnlyMemory<byte>>(value.Output.ScriptRef());
        Assert.Equal(expected[2], Convert.ToHexStringLower(CborSerializer.Deserialize<IScript>(bytes).Hash()));
    }

    /// <summary>Submitting the decoded recording is an exact native-byte passthrough.</summary>
    /// <returns>A task for the submission replay.</returns>
    [Fact]
    public async Task SubmissionPreservesTheRecordedBytes()
    {
        ReadTxResponse recorded = await LoadAsync("read-tx.pb", ReadTxResponse.Parser);
        PostMaryTransaction tx = CborSerializer.Deserialize<PostMaryTransaction>(recorded.Tx.NativeBytes.Memory);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Submit.Handler = (request, _) =>
        {
            Assert.Equal(recorded.Tx.NativeBytes, request.Tx.Raw);
            return Task.FromResult(new SubmitTxResponse { Ref = recorded.Tx.Cardano.Hash });
        };
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        Assert.Equal(Request("read-tx.pb")["ReadTx ".Length..], await provider.SubmitTransactionAsync(tx));
    }

    /// <summary>The captured statuses obey the provider's absence and rejection contracts.</summary>
    /// <returns>A task for each recorded probe.</returns>
    [Fact]
    public async Task StatusesPreserveAbsenceAndServerRejection()
    {
        string[] rows = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, Folder, "statuses.tsv"));
        Assert.Equal(4, rows.Length);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        for (int i = 1; i < rows.Length; i++)
        {
            string[] row = rows[i].Split('\t');
            Assert.Equal(4, row.Length);
            StatusCode code = (StatusCode)int.Parse(row[2], CultureInfo.InvariantCulture);
            switch (row[0])
            {
                case "ReadTx":
                    Assert.Equal(StatusCode.NotFound, code);
                    fake.TxHandler = (_, _) => throw new RpcException(new Status(code, row[3]));
                    Assert.Null(await provider.GetTransactionMetadataAsync(new string('0', 64)));
                    break;
                case "ReadUtxos":
                    Assert.Equal(StatusCode.OK, code);
                    Assert.Equal("OK, no items", row[3]);
                    fake.UtxosHandler = (_, _) => Task.FromResult(new ReadUtxosResponse());
                    Assert.Null(await provider.GetUtxoByOutRefAsync(new string('0', 64), 0));
                    break;
                case "SubmitTx":
                    Assert.NotEqual(StatusCode.OK, code);
                    fake.Submit.Handler = (_, _) => throw new RpcException(new Status(code, row[3]));
                    UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.SubmitTransactionAsync(TestData.Transaction()));
                    Assert.Equal(code, error.StatusCode);
                    Assert.Equal($"SubmitTx failed: {code}: {row[3]}", error.Message);
                    break;
                default: Assert.Fail("Unknown recorded probe."); break;
            }
        }
        Assert.Equal(3, fake.RequestCount);
    }

    private static void Equal(AnyUtxoData expected, ResolvedInput actual)
    {
        ParsedOutputs.Equal(expected.Cardano, actual.Output);
        Assert.True(expected.NativeBytes.Span.SequenceEqual(CborSerializer.Serialize(actual.Output)));
        Assert.True(expected.TxoRef.Hash.Span.SequenceEqual(actual.Outref.TransactionId.Span));
        Assert.Equal(expected.TxoRef.Index, actual.Outref.Index);
    }

    private static Task<byte[]> ReadAsync(string file) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, Folder, file));
    private static async Task<T> LoadAsync<T>(string file, MessageParser<T> parser) where T : IMessage<T> => parser.ParseFrom(await ReadAsync(file));
    private static string Request(string file) => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Golden/manifest.tsv"))
        .Select(line => line.Split('\t')).Single(row => row[0] == Folder + file)[4];
}
