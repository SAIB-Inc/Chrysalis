using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Xunit;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test.Providers;

public class BlockfrostRecordedTests
{
    private const string Canary = "CANARY-g9d4q7mq2s-redacted";

    [Fact]
    public async Task AddressPageMapsEveryOutput()
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows, (BlockfrostRecording.AddressPage, "02.json"));
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using JsonDocument recording = rows["02.json"].Json();
        List<ResolvedInput> actual = await provider.GetUtxosAsync(BlockfrostRecording.Address, timeout.Token);
        Assert.Equal(recording.RootElement.GetArrayLength(), actual.Count);
        foreach (JsonElement item in recording.RootElement.EnumerateArray())
        {
            string hash = item.GetProperty("tx_hash").GetString()!;
            ulong index = item.GetProperty("output_index").GetUInt64();
            ResolvedInput resolved = Assert.Single(actual, row => row.Outref.Index == index
                && Convert.ToHexStringLower(row.Outref.TransactionId.Span) == hash);
            ITransactionOutput output = resolved.Output;
            Assert.True(WalletAddress.FromBech32(item.GetProperty("address").GetString()!).ToBytes().AsSpan().SequenceEqual(output.Address().Span));
            JsonElement amounts = item.GetProperty("amount");
            foreach (JsonElement amount in amounts.EnumerateArray())
            {
                string unit = amount.GetProperty("unit").GetString()!;
                ulong quantity = ulong.Parse(amount.GetProperty("quantity").GetString()!, CultureInfo.InvariantCulture);
                Assert.Equal(quantity, unit == "lovelace" ? output.Amount().Lovelace()
                    : output.Amount().QuantityOf(Convert.FromHexString(unit[..56]), Convert.FromHexString(unit[56..])));
            }
            Assert.Equal(amounts.GetArrayLength() - 1, output.Amount().MultiAsset().Values.Sum(bundle => bundle.Value.Count));
            string? inline = item.GetProperty("inline_datum").GetString();
            Assert.Equal(inline, Hex(output.InlineDatumRaw()));
            Assert.Equal(inline is null ? item.GetProperty("data_hash").GetString() : null, Hex(output.DatumHash()));
            ReadOnlyMemory<byte>? script = output.ScriptRef();
            Assert.Equal(item.GetProperty("reference_script_hash").GetString(),
                script is { } bytes ? CborSerializer.Deserialize<IScript>(bytes).HashHex() : null);
        }
        // The address answer's null is unknown, not proof the output has no reference script.
        JsonElement reference = Assert.Single(recording.RootElement.EnumerateArray(), item =>
            item.GetProperty("tx_hash").GetString() == BlockfrostRecording.ReferenceTransaction && item.GetProperty("output_index").GetUInt64() == 0);
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("reference_script_hash").ValueKind);
        Assert.Equal((HttpMethod.Get, $"/api/v0/{BlockfrostRecording.AddressPage}"), Assert.Single(stub.Requests));
    }

    [Fact]
    public async Task TransactionLookupRetainsTheAddressPagesUnknownScript()
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using JsonDocument recording = rows["03.json"].Json();
        JsonElement entry = Assert.Single(recording.RootElement.GetProperty("outputs").EnumerateArray(), row => row.GetProperty("output_index").GetUInt64() == 0);
        Assert.Equal(BlockfrostRecording.ReferenceScript, entry.GetProperty("reference_script_hash").GetString());
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ResolvedInput resolved = Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(BlockfrostRecording.ReferenceTransaction, 0, timeout.Token));
        ReadOnlyMemory<byte> script = Assert.IsType<ReadOnlyMemory<byte>>(resolved.Output.ScriptRef());
        Assert.Equal(BlockfrostRecording.ReferenceScript, CborSerializer.Deserialize<IScript>(script).HashHex());
        string[] requests = [rows["03.json"].Path, rows["14.json"].Path, rows["15.json"].Path];
        Assert.Equal(requests, stub.Requests.Select(request => request.Uri["/api/v0/".Length..]));
    }

    [Fact]
    public async Task SpentOutputIsNotAUtxo()
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string hash = rows["13.json"].Path.Split('/')[1];
        Assert.Null(await provider.GetUtxoByOutRefAsync(hash, 1, timeout.Token));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task NativeScriptHashesToItsName()
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        const string hash = "1187b83cc346331269832af99b3cd1fe3e6c5c929fe2f31784310f49";
        MultiSigScript script = Assert.IsType<MultiSigScript>(await provider.GetScript(hash, timeout.Token));
        ScriptNOfK threshold = Assert.IsType<ScriptNOfK>(script.NativeScript);
        Assert.Equal(2, threshold.N);
        // Decoded independently from the witness set in block-139040159.cbor, tx 4.
        string[] keys = ["6e23d3de62a0f2820f0c6e9c462e366e9a1448c3d6970a1619f5460b",
            "63083bdf144e0976e5ac4dd23cd82f5d0493d7a46365c6c8f1f98999", "16c1554c34114687cfe699e548e1799c4a1dc17c76b869a632361863"];
        Assert.Equal(keys, threshold.Scripts.Select(item => Convert.ToHexStringLower(Assert.IsType<ScriptPubKey>(item).KeyHash.Span)));
        Assert.Equal(hash, script.HashHex());
    }

    [Fact]
    public async Task MetadataKeepsTypes()
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using JsonDocument recording = rows["21.json"].Json();
        byte[] cbor = Convert.FromHexString(recording.RootElement.GetProperty("cbor").GetString()!);
        // X7 requires an explicit transaction envelope; this oracle has four fields.
        PostMaryTransaction transaction = CborSerializer.Deserialize<PostMaryTransaction>(cbor);
        Metadata expected = Assert.IsType<Metadata>(transaction.AuxiliaryData?.Metadata());
        Metadata actual = Assert.IsType<Metadata>(await provider.GetTransactionMetadataAsync(rows["20.json"].Path.Split('/')[1], timeout.Token));
        MetadatumMap witness = Assert.IsType<MetadatumMap>(actual.Value[61284]);
        Assert.IsAssignableFrom<IMetadatumInt>(witness.Value[0].Key); // Old JSON endpoint made integer keys text.
        Assert.IsType<MetadatumBytes>(witness.Value[1].Value); // It made byte strings text too.
        Assert.Equal(expected.Value.Keys, actual.Value.Keys);
        foreach ((ulong label, ITransactionMetadatum value) in expected.Value) { EqualMetadatum(value, actual.Value[label]); }
        Assert.Equal($"/api/v0/{rows["20.json"].Path}", Assert.Single(stub.Requests).Uri);
    }

    private static void EqualMetadatum(ITransactionMetadatum expected, ITransactionMetadatum actual)
    {
        switch (expected)
        {
            case MetadatumMap map:
                List<KeyValuePair<ITransactionMetadatum, ITransactionMetadatum>> pairs = Assert.IsType<MetadatumMap>(actual).Value;
                Assert.Equal(map.Value.Count, pairs.Count);
                for (int i = 0; i < pairs.Count; i++)
                {
                    EqualMetadatum(map.Value[i].Key, pairs[i].Key);
                    EqualMetadatum(map.Value[i].Value, pairs[i].Value);
                }
                break;
            case MetadatumList list:
                List<ITransactionMetadatum> items = Assert.IsType<MetadatumList>(actual).Value;
                Assert.Equal(list.Value.Count, items.Count);
                foreach ((ITransactionMetadatum left, ITransactionMetadatum right) in list.Value.Zip(items)) { EqualMetadatum(left, right); }
                break;
            case MetadatumBytes bytes: Assert.Equal(bytes.Value, Assert.IsType<MetadatumBytes>(actual).Value); break;
            case MetadataText text: Assert.Equal(text.Value, Assert.IsType<MetadataText>(actual).Value); break;
            case MetadatumIntLong signed: Assert.Equal(signed.Value, Assert.IsType<MetadatumIntLong>(actual).Value); break;
            case MetadatumIntUlong unsigned: Assert.Equal(unsigned.Value, Assert.IsType<MetadatumIntUlong>(actual).Value); break;
            default: throw new InvalidOperationException("Unknown transaction metadatum type.");
        }
    }

    [Theory]
    [InlineData("22.json")]
    [InlineData("23.json")]
    [InlineData("24.json")]
    [InlineData("25.json")]
    public async Task RealErrorBodies(string file)
    {
        Dictionary<string, BlockfrostRecording.Row> rows = BlockfrostRecording.Load();
        using StubHttpHandler stub = BlockfrostRecording.Handler(rows);
        using Blockfrost provider = new(Canary, NetworkType.Preprod, handler: stub);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        if (file == "22.json") { Assert.Empty(await provider.GetUtxosAsync(rows[file].Path.Split('/')[1], timeout.Token)); }
        else if (file == "23.json") { Assert.Null(await provider.GetUtxoByOutRefAsync(rows[file].Path.Split('/')[1], 0, timeout.Token)); }
        else
        {
            ProviderStatusException error = await Assert.ThrowsAsync<ProviderStatusException>(async () =>
            {
                if (file == "24.json") { _ = await provider.GetParametersAsync(timeout.Token); }
                else { _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token); }
            });
            Assert.Equal(file == "24.json" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, error.StatusCode);
            Assert.False(error.IsTransient);
            ProviderHttpTests.AssertNoCanary(error, Canary);
        }
        Assert.Single(stub.Requests);
    }

    [Fact]
    public void RecordingIsCompleteAndHoldsNoKey()
    {
        const string relative = "src/Chrysalis.Tx.Test/TestData/providers";
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, relative))) { root = root.Parent; }
        Assert.NotNull(root);
        string provider = Path.Combine(root.FullName, relative);
        string fixtures = Path.Combine(provider, "blockfrost");
        _ = BlockfrostRecording.Load(fixtures);
        Regex key = new("(mainnet|preprod|preview|ipfs)[A-Za-z0-9]{32}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (string path in Directory.EnumerateFiles(fixtures)) { Check(path, true); }
        Check(Path.Combine(provider, "record-blockfrost.sh"), false);
        Check(Path.Combine(provider, "record-blockfrost.py"), false);
        void Check(string path, bool checkName)
        {
            int number = 0;
            foreach (string line in File.ReadLines(path))
            {
                number++;
                Assert.False(key.IsMatch(line) || (checkName && line.Contains("project_id", StringComparison.OrdinalIgnoreCase)),
                    $"Forbidden marker: {Path.GetFileName(path)}:{number}");
            }
        }
    }

    private static string? Hex(ReadOnlyMemory<byte>? value) => value is { IsEmpty: false } bytes ? Convert.ToHexStringLower(bytes.Span) : null;
}
