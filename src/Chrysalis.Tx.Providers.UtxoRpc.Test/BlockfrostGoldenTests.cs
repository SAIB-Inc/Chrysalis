using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Providers;
using Chrysalis.Wallet.Models.Enums;
using Utxorpc.V1Beta.Query;
using Xunit;
using Xunit.Sdk;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares recorded Blockfrost replies with independently captured ledger data.</summary>
public sealed class BlockfrostGoldenTests
{
    private const string DolosFolder = "Golden/dolos-1.6.3-preprod/";

    /// <summary>Matches all parameter fields against the node at epoch 325.</summary>
    /// <returns>A task for the recorded parameter request and comparison.</returns>
    [Fact]
    public async Task ParametersMatchTheNodeAtEpoch325()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Golden/cardano-node-11.1.3-preprod/protocol-parameters.json"), timeout.Token));
        using StubHttpHandler stub = BlockfrostRecording.Handler(BlockfrostRecording.Load(), ("epochs/latest/parameters", "18.json"));
        using Blockfrost provider = new("CANARY-blockfrost-golden", NetworkType.Preprod, handler: stub);
        ProtocolParams parameters = await provider.GetParametersAsync(timeout.Token);
        foreach (JsonProperty property in expected.RootElement.EnumerateObject())
        {
            using JsonDocument field = JsonSerializer.SerializeToDocument(new Dictionary<string, JsonElement> { [property.Name] = property.Value });
            try { LedgerParameters.Equal(field.RootElement, parameters); }
            catch (XunitException error) { throw new XunitException($"{property.Name}: {error.Message}"); }
        }
        _ = Assert.Single(stub.Requests);
    }

    /// <summary>Matches the still-unspent Dolos outputs, including datum and script hashes.</summary>
    /// <returns>A task for the recorded out-ref reads and comparison.</returns>
    [Fact]
    public async Task OutputsMatchTheDolosCapture()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        Dictionary<string, BlockfrostRecording.Row> paths = rows.Values.ToDictionary(row => row.Path);
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new("CANARY-blockfrost-golden", NetworkType.Preprod, handler: stub);
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc dolos = new(fake.Endpoint, NetworkType.Preprod);
        List<ResolvedInput> expected = [], actual = [];
        bool keepsReferenceScript = false, keepsDatumHash = false;
        string[] manifest = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Golden/manifest.tsv"), timeout.Token);
        foreach (string file in new[] { "read-utxos.pb", "read-hash-utxos.pb" })
        {
            string filename = DolosFolder + file;
            string request = manifest.Select(line => line.Split('\t')).Single(row => row[0] == filename)[4];
            ReadUtxosResponse read = ReadUtxosResponse.Parser.ParseFrom(
                await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, filename), timeout.Token));
            fake.UtxosHandler = (_, _) => Task.FromResult(read);
            foreach (string outref in request["ReadUtxos ".Length..].Split(','))
            {
                string[] parts = outref.Split('#');
                string hash = parts[0];
                uint index = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                using JsonDocument body = paths[$"txs/{hash}/utxos"].Json();
                if (!body.RootElement.GetProperty("outputs").EnumerateArray().Any(item =>
                    item.GetProperty("output_index").GetUInt64() == index
                    && item.GetProperty("collateral").ValueKind == JsonValueKind.False
                    && item.TryGetProperty("consumed_by_tx", out JsonElement consumed) && consumed.ValueKind == JsonValueKind.Null)) { continue; }

                expected.Add(Assert.IsType<ResolvedInput>(await dolos.GetUtxoByOutRefAsync(hash, index, timeout.Token)));
                actual.Add(Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(hash, index, timeout.Token)));
                keepsReferenceScript |= hash == BlockfrostRecording.ReferenceTransaction && index == 0;
                keepsDatumHash |= file == "read-hash-utxos.pb";
            }
        }
        Assert.True(keepsReferenceScript, "No unspent script-bearing reference from the Dolos capture.");
        Assert.True(keepsDatumHash, "No unspent hash-datum reference from the Dolos capture.");
        ParityTests.EqualOutputs(expected, actual);
    }

    /// <summary>Non-terminating ledger ratios match Blockfrost's published precision while terminating ratios stay exact.</summary>
    [Fact]
    public void ParityRatioComparisonHandlesNonTerminatingLedgerRatios()
    {
        CborRationalNumber twoThirds = CborRationalNumber.Create(2, 3);
        ParityTests.EqualValue(ProviderValues.ExactRational(0.6666666666666666m), twoThirds, "dRepVotingThresholds.committeeNormal");
        _ = Assert.Throws<TrueException>(() =>
            ParityTests.EqualValue(ProviderValues.ExactRational(0.67m), twoThirds, "dRepVotingThresholds.committeeNormal"));
        _ = Assert.Throws<TrueException>(() =>
            ParityTests.EqualValue(ProviderValues.ExactRational(0.6666666666666665m), twoThirds, "dRepVotingThresholds.committeeNormal"));

        CborRationalNumber terminating = CborRationalNumber.Create(51, 100);
        ParityTests.EqualValue(ProviderValues.ExactRational(0.51m), terminating, "poolVotingThresholds.committeeNormal");
        // Same IEEE-754 double as 0.51, but a terminating ledger ratio must still match exactly.
        _ = Assert.Throws<TrueException>(() =>
            ParityTests.EqualValue(ProviderValues.ExactRational(0.51000000000000001m), terminating, "poolVotingThresholds.committeeNormal"));
    }
}
