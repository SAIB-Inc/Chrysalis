using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.V1Beta.Query;
using Utxorpc.V1Beta.Submit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Address = Chrysalis.Wallet.Models.Addresses.Address;
using GrpcMetadata = Grpc.Core.Metadata;
using Spec = Utxorpc.V1Beta.Cardano;
using SubmitChainTx = Utxorpc.V1Beta.Submit.AnyChainTx;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Records public third-party chain answers only on explicit opt-in.</summary>
/// <param name="output">Receives public capture tips for the provenance manifest.</param>
[Trait("Category", "Integration")]
[Collection("Live")]
public sealed class Recorder(ITestOutputHelper output)
{
    /// <summary>Captures the four answers and the three harmless status probes.</summary>
    /// <returns>A task for the local capture.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network, LiveSettings.Addresses, LiveSettings.RecordDirectory, LiveSettings.RecordAddress, LiveSettings.RecordTransaction)]
    public async Task RecordsPublicAnswers()
    {
        _ = LiveSettings.ReadNetwork();
        string address = LiveSettings.Require(LiveSettings.RecordAddress);
        Assert.DoesNotContain(address, LiveSettings.WalletAddresses());
        string directory = LiveSettings.Require(LiveSettings.RecordDirectory);
        using GrpcChannel channel = GrpcChannel.ForAddress(LiveSettings.Require(LiveSettings.Endpoint), new GrpcChannelOptions { MaxReceiveMessageSize = null });
        QueryService.QueryServiceClient query = new(channel);
        SubmitService.SubmitServiceClient submit = new(channel);
        GrpcMetadata headers = [];
        foreach ((string name, string value) in LiveSettings.HeaderValues()) { headers.Add(name, value); }
        CallOptions options = new(headers, DateTime.UtcNow.AddSeconds(30));
        ReadParamsResponse parameters = await query.ReadParamsAsync(new(), options);
        SearchUtxosResponse search = await query.SearchUtxosAsync(new SearchUtxosRequest
        {
            Predicate = new UtxoPredicate
            {
                Match = new AnyUtxoPattern
                {
                    Cardano = new Spec.TxOutputPattern
                    { Address = new Spec.AddressPattern { ExactAddress = ByteString.CopyFrom(Address.FromBech32(address).ToBytes()) } }
                }
            }
        }, options);
        Assert.InRange(search.Items.Count, 1, 49);
        Assert.False(search.HasNextToken && search.NextToken.Length != 0);
        ReadUtxosRequest references = new();
        for (int i = 0; i < search.Items.Count; i++) { references.Keys.Add(search.Items[i].TxoRef); }
        ReadUtxosResponse byReference = await query.ReadUtxosAsync(references, options);
        ReadTxResponse transaction = await query.ReadTxAsync(new ReadTxRequest
        { Hash = ByteString.CopyFrom(Convert.FromHexString(LiveSettings.Require(LiveSettings.RecordTransaction))) }, options);
        _ = Directory.CreateDirectory(directory);
        await WriteAsync("read-params.pb", parameters, parameters.LedgerTip);
        await WriteAsync("search-utxos.pb", search, search.LedgerTip);
        await WriteAsync("read-utxos.pb", byReference, byReference.LedgerTip);
        await WriteAsync("read-tx.pb", transaction, transaction.LedgerTip);
        if (LiveSettings.Read(LiveSettings.RecordHashReferences) is { Length: > 0 } hashReferences)
        {
            ReadUtxosRequest hashed = new();
            foreach (string reference in hashReferences.Split(','))
            {
                string[] parts = reference.Split('#');
                hashed.Keys.Add(new TxoRef { Hash = ByteString.CopyFrom(Convert.FromHexString(parts[0])), Index = uint.Parse(parts[1], CultureInfo.InvariantCulture) });
            }
            ReadUtxosResponse hashDatums = await query.ReadUtxosAsync(hashed, options);
            Assert.Equal(hashed.Keys.Count, hashDatums.Items.Count);
            await WriteAsync("read-hash-utxos.pb", hashDatums, hashDatums.LedgerTip);
        }
        ByteString unknown = ByteString.CopyFrom(new byte[32]);
        List<string> statuses = ["operation\tcase\tstatus\ttext"];
        try
        {
            _ = await query.ReadTxAsync(new ReadTxRequest { Hash = unknown }, options);
            Assert.Fail("ReadTx unexpectedly accepted the zero hash.");
        }
        catch (RpcException error) { statuses.Add(Line("ReadTx", "zero hash", error)); }
        ReadUtxosResponse missing = await query.ReadUtxosAsync(new ReadUtxosRequest { Keys = { new TxoRef { Hash = unknown } } }, options);
        Assert.Empty(missing.Items);
        statuses.Add("ReadUtxos\tzero reference\t0\tOK, no items");
        try
        {
            _ = await submit.SubmitTxAsync(new SubmitTxRequest { Tx = new SubmitChainTx { Raw = ByteString.CopyFrom([0]) } }, options);
            Assert.Fail("SubmitTx unexpectedly accepted malformed bytes.");
        }
        catch (RpcException error) { statuses.Add(Line("SubmitTx", "one byte", error)); }
        await File.WriteAllLinesAsync(Path.Combine(directory, "statuses.tsv"), statuses);

        async Task WriteAsync(string name, IMessage message, ChainPoint tip)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, name), message.ToByteArray());
            output.WriteLine("{0}: tip slot={1} hash={2} captured={3:O}", name, tip.Slot, Convert.ToHexStringLower(tip.Hash.Span), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Recaptures only the immutable reference-script hash inside this test process.</summary>
    /// <returns>A task for one Blockfrost request, preserving the expected hash.</returns>
    [LiveFact(LiveSettings.Network, LiveSettings.BlockfrostKeyFile, LiveSettings.RecordDirectory)]
    public async Task RecordsReferenceScriptHash()
    {
        try
        {
            _ = LiveSettings.ReadNetwork();
            string target = Path.Combine(LiveSettings.Require(LiveSettings.RecordDirectory), "refscript-hash.tsv");
            string[] original = await File.ReadAllLinesAsync(target);
            Assert.Equal(2, original.Length);
            Assert.Equal("utc\tendpoint\tscript_hash", original[0]);
            string[] fields = original[1].Split('\t');
            Assert.Equal(3, fields.Length);
            string[] request = fields[1].Split(' ');
            Assert.Equal("GET", request[0]);
            Assert.StartsWith("/txs/", request[1], StringComparison.Ordinal);
            Assert.EndsWith("/utxos", request[1], StringComparison.Ordinal);
            uint index = uint.Parse(request[2]["output_index=".Length..], CultureInfo.InvariantCulture);
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("project_id", (await File.ReadAllTextAsync(LiveSettings.Require(LiveSettings.BlockfrostKeyFile))).Trim());
            using HttpResponseMessage response = await client.GetAsync(new Uri("https://cardano-preprod.blockfrost.io/api/v0" + request[1]), HttpCompletionOption.ResponseHeadersRead);
            _ = response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            using JsonDocument answer = await JsonDocument.ParseAsync(stream);
            foreach (JsonElement item in answer.RootElement.GetProperty("outputs").EnumerateArray())
            {
                if (item.GetProperty("output_index").GetUInt32() != index) { continue; }
                string? hash = item.GetProperty("reference_script_hash").GetString();
                Assert.True(hash is { Length: 56 } && hash.All(char.IsAsciiHexDigit), "Unexpected reference-script hash format.");
                Assert.Equal(fields[2], hash);
                await File.WriteAllLinesAsync(target, [original[0], FormattableString.Invariant($"{DateTimeOffset.UtcNow:O}\t{fields[1]}\t{hash}")]);
                return;
            }
            Assert.Fail("The immutable reference-script output was not returned.");
        }
        catch (XunitException) { throw; }
        catch (Exception) { throw new XunitException("Reference-script hash capture failed; details suppressed."); }
    }

    private static string Line(string operation, string probe, RpcException error) =>
        $"{operation}\t{probe}\t{((int)error.StatusCode).ToString(CultureInfo.InvariantCulture)}\t{error.Status.Detail.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}";
}
