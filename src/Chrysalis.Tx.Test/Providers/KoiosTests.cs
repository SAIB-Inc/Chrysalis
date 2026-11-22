using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Providers.UtxoRpcTests;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Wallet.Models.Enums;
using Xunit;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test.Providers;

#pragma warning disable JSON002 // Test fixtures include JSON fragments or malformed JSON.
public class KoiosTests
{
    private const string MainAddress = "addr_test1vrapzvl8gvavjfy2d7qa4xvt8a6ytcsrq4ykr6azj27mylg59qvqy";
    private const string ScriptAddress = "addr_test1wqvxuvh64q9zdqgrjt76d42eclk5wgdxtnsun4808cwg0dqxy2mj0";
    internal const string PlutusV3Hash = "186e32faa80a26810392fda6d559c7ed4721a65ce1c9d4ef3e1c87b4";
    internal const string NativeHash = "751ebae7b78b2be4c0b6283f708becce05a802966b38b7f31d8758b2";
    internal static readonly Lazy<CliVectors> Vectors = new(() => CliVectors.Load(CliVectors.Root));
    private static readonly Lazy<string> FixtureJson = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "providers", "koios", "address_utxos.json")));

    private static Koios Create(StubHttpHandler stub, string? token = null, string url = "https://koios.test/api/v1/") =>
        new(NetworkType.Preprod, token, url, stub);

    private static JsonObject FixtureRow(int index) => JsonNode.Parse(FixtureJson.Value)!.AsArray()[index]!.DeepClone().AsObject();

    private static string MutateRow(int index, Action<JsonObject> mutate)
    {
        JsonObject row = FixtureRow(index);
        mutate(row);
        return new JsonArray(row).ToJsonString();
    }

    private static string MutateParams(Action<JsonObject> mutate)
    {
        JsonObject root = JsonNode.Parse(Vectors.Value.Bytes(CliVectors.G1))!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }

    private static string RowJson(int index, string address = MainAddress) =>
        $$"""{"tx_hash":"{{BlockfrostTests.Hash}}","tx_index":{{index.ToString(CultureInfo.InvariantCulture)}},"address":"{{address}}","value":"2000000","asset_list":[],"is_spent":false}""";

    private static int QueryInt(Uri uri, string key) => int.Parse(
        uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Single(pair => pair[0] == key)[1],
        CultureInfo.InvariantCulture);

    private static StubHttpHandler CappedPagingStub(int total, int cap, int? failAtOffset = null, bool ignoreOffset = false, int? oversize = null) =>
        new(request =>
        {
            int offset = QueryInt(request.RequestUri!, "offset");
            int limit = QueryInt(request.RequestUri!, "limit");
            if (failAtOffset == offset) { return StubHttpHandler.Json("server error", HttpStatusCode.InternalServerError); }
            if (oversize is { } count)
            {
                return StubHttpHandler.Json("[" + string.Join(",", Enumerable.Range(0, count).Select(i => RowJson(i))) + "]");
            }
            int start = ignoreOffset ? 0 : offset;
            int take = Math.Max(0, Math.Min(total - start, Math.Min(limit, cap)));
            return StubHttpHandler.Json("[" + string.Join(",", Enumerable.Range(start, take).Select(i => RowJson(i))) + "]");
        });

    [Fact]
    public async Task UtxosMapEveryKindOfOutput()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        List<(string PathAndQuery, string Body, bool HasAuth)> calls = [];
        using StubHttpHandler stub = new(async (request, ct) =>
        {
            string body = await request.Content!.ReadAsStringAsync(ct);
            calls.Add((request.RequestUri!.PathAndQuery, body, request.Headers.Authorization is not null));
            return StubHttpHandler.Json(calls.Count == 1 ? FixtureJson.Value : "[]");
        });
        using Koios koios = Create(stub);
        List<ResolvedInput> results = await koios.GetUtxosAsync([MainAddress], timeout.Token);

        Assert.Equal(2, calls.Count);
        Assert.Equal("/api/v1/address_utxos?offset=0&limit=1000&order=tx_hash.asc,tx_index.asc", calls[0].PathAndQuery);
        Assert.Equal("/api/v1/address_utxos?offset=7&limit=1000&order=tx_hash.asc,tx_index.asc", calls[1].PathAndQuery);
        string expectedBody = $$"""{"_addresses":["{{MainAddress}}"],"_extended":true}""";
        Assert.All(calls, call =>
        {
            Assert.Equal(expectedBody, call.Body);
            Assert.False(call.HasAuth);
        });

        Assert.Equal(7, results.Count);
        byte[] mainBytes = WalletAddress.FromBech32(MainAddress).ToBytes();
        byte[] scriptBytes = WalletAddress.FromBech32(ScriptAddress).ToBytes();

        // Row 0: plain ADA from string
        Assert.Equal(new string('1', 64), Convert.ToHexStringLower(results[0].Outref.TransactionId.Span));
        Assert.Equal(0UL, results[0].Outref.Index);
        Assert.Equal(mainBytes, results[0].Output.Address().ToArray());
        Assert.Equal(2_000_000UL, results[0].Output.Amount().Lovelace());
        Assert.Empty(results[0].Output.Amount().MultiAsset());
        Assert.Null(Assert.IsType<PostAlonzoTransactionOutput>(results[0].Output).Datum);
        Assert.Null(results[0].Output.ScriptRef());

        // Row 1: multi-asset from number, empty asset name, u64-max quantity
        Assert.Equal(1UL, results[1].Outref.Index);
        Assert.Equal(3_500_000UL, results[1].Output.Amount().Lovelace());
        Dictionary<string, TokenBundleOutput> assets = results[1].Output.Amount().MultiAsset();
        Assert.Equal(2, assets.Count);
        KeyValuePair<ReadOnlyMemory<byte>, ulong> emptyAsset = Assert.Single(assets[new string('1', 56)].Value);
        Assert.Empty(emptyAsset.Key.ToArray());
        Assert.Equal(ulong.MaxValue, emptyAsset.Value);
        KeyValuePair<ReadOnlyMemory<byte>, ulong> namedAsset = Assert.Single(assets[new string('2', 56)].Value);
        Assert.Equal("746f6b656e", Convert.ToHexStringLower(namedAsset.Key.Span));
        Assert.Equal(42UL, namedAsset.Value);

        // Row 2: datum hash only
        DatumHashOption hashDatum = Assert.IsType<DatumHashOption>(Assert.IsType<PostAlonzoTransactionOutput>(results[2].Output).Datum);
        Assert.Equal(new string('a', 64), Convert.ToHexStringLower(hashDatum.DatumHash.Span));

        // Row 3: inline datum preferred over datum_hash
        InlineDatumOption inlineDatum = Assert.IsType<InlineDatumOption>(Assert.IsType<PostAlonzoTransactionOutput>(results[3].Output).Datum);
        Assert.Equal("182a", Convert.ToHexStringLower(inlineDatum.Data.Value.Span));

        // Rows 4 and 5: current and legacy PlutusV3 forms produce equal script bytes
        Assert.Equal(scriptBytes, results[4].Output.Address().ToArray());
        Assert.Equal(scriptBytes, results[5].Output.Address().ToArray());
        ReadOnlyMemory<byte> v3Current = Assert.NotNull(results[4].Output.ScriptRef());
        ReadOnlyMemory<byte> v3Legacy = Assert.NotNull(results[5].Output.ScriptRef());
        Assert.Equal(v3Current.ToArray(), v3Legacy.ToArray());
        PlutusV3Script decodedV3 = Assert.IsType<PlutusV3Script>(CborSerializer.Deserialize<IScript>(v3Current));
        Assert.Equal(PlutusV3Hash, decodedV3.HashHex());

        // Row 6: native timelock script
        ReadOnlyMemory<byte> nativeRaw = Assert.NotNull(results[6].Output.ScriptRef());
        MultiSigScript decodedNative = Assert.IsType<MultiSigScript>(CborSerializer.Deserialize<IScript>(nativeRaw));
        Assert.Equal(NativeHash, decodedNative.HashHex());
    }

    [Theory]
    [InlineData("uncapped")]
    [InlineData("capped")]
    [InlineData("capped-500")]
    [InlineData("oversize")]
    [InlineData("ignores-offset")]
    [InlineData("shared-output")]
    [InlineData("repeated-address")]
    public async Task PagingReadsEveryRow(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using StubHttpHandler stub = scenario switch
        {
            "uncapped" => CappedPagingStub(1001, 1000),
            "capped" => CappedPagingStub(201, 100),
            "capped-500" => CappedPagingStub(201, 100, failAtOffset: 100),
            "oversize" => CappedPagingStub(1001, 1001, oversize: 1001),
            "ignores-offset" => CappedPagingStub(2, 2, ignoreOffset: true),
            _ => CappedPagingStub(1, 1000)
        };
        using Koios koios = Create(stub);
        List<string> addresses = scenario switch
        {
            "shared-output" => [MainAddress, ScriptAddress],
            "repeated-address" => [MainAddress, MainAddress],
            _ => [MainAddress]
        };

        if (scenario == "capped-500")
        {
            ProviderStatusException status = await Assert.ThrowsAsync<ProviderStatusException>(() => koios.GetUtxosAsync(addresses, timeout.Token));
            Assert.Equal(HttpStatusCode.InternalServerError, status.StatusCode);
            Assert.Equal(2, stub.Requests.Count);
            return;
        }
        if (scenario is "oversize" or "ignores-offset")
        {
            ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => koios.GetUtxosAsync(addresses, timeout.Token));
            Assert.False(error.IsTransient);
            Assert.Equal(scenario == "oversize" ? 1 : 2, stub.Requests.Count);
            return;
        }

        List<ResolvedInput> results = await koios.GetUtxosAsync(addresses, timeout.Token);
        int expectedCount = scenario switch { "uncapped" => 1001, "capped" => 201, _ => 1 };
        Assert.Equal(Enumerable.Range(0, expectedCount).Select(i => (ulong)i), results.Select(item => item.Outref.Index));
        int[] expectedOffsets = scenario switch
        {
            "uncapped" => [0, 1000, 1001],
            "capped" => [0, 100, 200, 201],
            "shared-output" => [0, 1, 0, 1],
            _ => [0, 1]
        };
        Assert.Equal(expectedOffsets, stub.Requests.Select(req => QueryInt(new Uri("https://koios.test" + req.Uri), "offset")));
    }

    [Fact]
    public async Task OutrefReadsOneUnspentOutput()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string lowerHash = new('1', 64);
        string lowerAlpha = new('a', 64);
        string upperAlpha = new('A', 64);
        List<string> bodies = [];
        int call = 0;
        using StubHttpHandler stub = new(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return StubHttpHandler.Json(++call switch
            {
                1 => new JsonArray(FixtureRow(1)).ToJsonString(),
                2 => MutateRow(1, row => row["is_spent"] = true),
                3 => "[]",
                _ => MutateRow(1, row => row["tx_hash"] = lowerAlpha)
            });
        });
        using Koios koios = Create(stub);

        ResolvedInput unspent = Assert.IsType<ResolvedInput>(await koios.GetUtxoByOutRefAsync(lowerHash, 1, timeout.Token));
        Assert.Equal(1UL, unspent.Outref.Index);
        Assert.Equal(3_500_000UL, unspent.Output.Amount().Lovelace());
        Assert.Equal($$"""{"_utxo_refs":["{{lowerHash}}#1"],"_extended":true}""", bodies[0]);

        Assert.Null(await koios.GetUtxoByOutRefAsync(lowerHash, 1, timeout.Token));
        Assert.Null(await koios.GetUtxoByOutRefAsync(lowerHash, 1, timeout.Token));

        ResolvedInput upperMatch = Assert.IsType<ResolvedInput>(await koios.GetUtxoByOutRefAsync(upperAlpha, 1, timeout.Token));
        Assert.Equal(lowerAlpha, Convert.ToHexStringLower(upperMatch.Outref.TransactionId.Span));
    }

    [Theory]
    [InlineData("utxos-empty")]
    [InlineData("metadata-empty")]
    [InlineData("metadata-f01")]
    [InlineData("utxos-404")]
    [InlineData("params-404")]
    [InlineData("outref-404")]
    [InlineData("metadata-404")]
    public async Task AbsenceIsOnlyAnEmptyAnswer(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string f01Hex = Convert.ToHexStringLower(Vectors.Value.Bytes("cases/F01.tx"));
        using StubHttpHandler stub = new(_ => scenario switch
        {
            "utxos-empty" or "metadata-empty" => StubHttpHandler.Json("[]"),
            "metadata-f01" => StubHttpHandler.Json($$"""[{"tx_hash":"{{BlockfrostTests.Hash}}","cbor":"{{f01Hex}}"}]"""),
            _ => StubHttpHandler.Json("not found", HttpStatusCode.NotFound)
        });
        using Koios koios = Create(stub);

        switch (scenario)
        {
            case "utxos-empty":
                Assert.Empty(await koios.GetUtxosAsync([MainAddress], timeout.Token));
                break;
            case "metadata-empty":
            case "metadata-f01":
                Assert.Null(await koios.GetTransactionMetadataAsync(BlockfrostTests.Hash, timeout.Token));
                break;
            default:
                Task Call() => scenario switch
                {
                    "utxos-404" => koios.GetUtxosAsync([MainAddress], timeout.Token),
                    "params-404" => koios.GetParametersAsync(timeout.Token),
                    "outref-404" => koios.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 0, timeout.Token),
                    _ => koios.GetTransactionMetadataAsync(BlockfrostTests.Hash, timeout.Token)
                };
                Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<ProviderStatusException>(Call)).StatusCode);
                break;
        }
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("missing-tx-hash")]
    [InlineData("missing-address")]
    [InlineData("missing-value")]
    [InlineData("negative-tx-index")]
    [InlineData("fractional-value")]
    [InlineData("bad-quantity")]
    [InlineData("null-asset-list")]
    [InlineData("repeated-asset")]
    [InlineData("null-inline-datum-bytes")]
    [InlineData("short-datum-hash")]
    [InlineData("plutus-v4")]
    [InlineData("multisig-type")]
    [InlineData("plutus-hash-mismatch")]
    [InlineData("plutus-truncated-cbor")]
    [InlineData("plutus-trailing-byte")]
    [InlineData("native-hash-mismatch")]
    [InlineData("native-unknown-type")]
    [InlineData("outref-other-output")]
    [InlineData("outref-wrong-hash")]
    [InlineData("outref-two-rows")]
    [InlineData("outref-missing-is-spent")]
    [InlineData("cbor-null")]
    [InlineData("cbor-wrong-hash")]
    [InlineData("cbor-truncated")]
    [InlineData("partial-page")]
    [InlineData("params-missing-fee")]
    [InlineData("params-unknown-cost-model")]
    [InlineData("params-negative-ratio")]
    [InlineData("params-zero-denominator")]
    [InlineData("params-null-body")]
    public async Task RejectsUnusableAnswers(string scenario)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string f04Hex = Convert.ToHexStringLower(Vectors.Value.Bytes("cases/F04.tx"));
        string json = scenario switch
        {
            "missing-tx-hash" => MutateRow(0, row => _ = row.Remove("tx_hash")),
            "missing-address" => MutateRow(0, row => _ = row.Remove("address")),
            "missing-value" => MutateRow(0, row => _ = row.Remove("value")),
            "negative-tx-index" => MutateRow(0, row => row["tx_index"] = -1),
            "fractional-value" => MutateRow(0, row => row["value"] = "1.5"),
            "bad-quantity" => MutateRow(1, row => row["asset_list"]![0]!["quantity"] = "x"),
            "null-asset-list" => MutateRow(0, row => row["asset_list"] = null),
            "repeated-asset" => MutateRow(1, row => row["asset_list"]!.AsArray().Add(row["asset_list"]![0]!.DeepClone())),
            "null-inline-datum-bytes" => MutateRow(0, row => row["inline_datum"] = new JsonObject { ["bytes"] = null }),
            "short-datum-hash" => MutateRow(0, row => row["datum_hash"] = new string('a', 62)),
            "plutus-v4" => MutateRow(4, row => row["reference_script"]!["type"] = "plutusV4"),
            "multisig-type" => MutateRow(6, row => row["reference_script"]!["type"] = "multisig"),
            "plutus-hash-mismatch" => MutateRow(4, row => row["reference_script"]!["hash"] = new string('0', 56)),
            "plutus-truncated-cbor" => MutateRow(4, row => row["reference_script"]!["bytes"] = "5f"),
            "plutus-trailing-byte" => MutateRow(5, row => row["reference_script"]!["bytes"] = "4645010100249900"),
            "native-hash-mismatch" => MutateRow(6, row => row["reference_script"]!["hash"] = new string('0', 56)),
            "native-unknown-type" => MutateRow(6, row => row["reference_script"]!["value"] = new JsonObject { ["type"] = "unknown" }),
            "outref-other-output" => new JsonArray(FixtureRow(0)).ToJsonString(),
            "outref-wrong-hash" => MutateRow(1, row => row["tx_hash"] = new string('2', 64)),
            "outref-two-rows" => new JsonArray(FixtureRow(0), FixtureRow(1)).ToJsonString(),
            "outref-missing-is-spent" => MutateRow(0, row => _ = row.Remove("is_spent")),
            "cbor-null" => $$"""[{"tx_hash":"{{BlockfrostTests.Hash}}","cbor":null}]""",
            "cbor-wrong-hash" => $$"""[{"tx_hash":"{{new string('1', 64)}}","cbor":"{{f04Hex}}"}]""",
            "cbor-truncated" => $$"""[{"tx_hash":"{{BlockfrostTests.Hash}}","cbor":"8400"}]""",
            "partial-page" => new JsonArray(FixtureRow(0), new JsonObject()).ToJsonString(),
            "params-missing-fee" => MutateParams(root => _ = root.Remove("txFeeFixed")),
            "params-unknown-cost-model" => MutateParams(root => root["costModels"]!["PlutusV9"] = new JsonArray(0)),
            "params-negative-ratio" => MutateParams(root => root["executionUnitPrices"]!["priceMemory"] = -1),
            "params-zero-denominator" => MutateParams(root => root["monetaryExpansion"] = new JsonObject { ["numerator"] = 1, ["denominator"] = 0 }),
            _ => "null"
        };
        int calls = 0;
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(++calls == 1 ? json : "[]"));
        using Koios koios = Create(stub);

        Task Call() => scenario switch
        {
            "outref-other-output" or "outref-wrong-hash" => koios.GetUtxoByOutRefAsync(new string('1', 64), 1, timeout.Token),
            "outref-two-rows" or "outref-missing-is-spent" => koios.GetUtxoByOutRefAsync(new string('1', 64), 0, timeout.Token),
            "cbor-null" or "cbor-wrong-hash" or "cbor-truncated" => koios.GetTransactionMetadataAsync(BlockfrostTests.Hash, timeout.Token),
            "params-missing-fee" or "params-unknown-cost-model" or "params-negative-ratio"
                or "params-zero-denominator" or "params-null-body" => koios.GetParametersAsync(timeout.Token),
            _ => koios.GetUtxosAsync([MainAddress], timeout.Token)
        };

        ProviderException error = await Assert.ThrowsAsync<ProviderException>(Call);
        Assert.False(error.IsTransient);
        Assert.StartsWith("Koios ", error.Message, StringComparison.Ordinal);
        Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ParametersAreTheNodesAnswer()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        byte[] g1Bytes = Vectors.Value.Bytes(CliVectors.G1);
        string objectRatioJson = MutateParams(root => root["monetaryExpansion"] = new JsonObject { ["numerator"] = 1, ["denominator"] = 3 });
        int call = 0;
        using StubHttpHandler stub = new(_ => StubHttpHandler.Json(++call == 1 ? Encoding.UTF8.GetString(g1Bytes) : objectRatioJson));
        using Koios koios = Create(stub);

        using JsonDocument g1 = JsonDocument.Parse(g1Bytes);
        ProtocolParams koiosParams = await koios.GetParametersAsync(timeout.Token);
        LedgerParameters.Equal(g1.RootElement, koiosParams);
        LedgerParameters.Equal(g1.RootElement, Vectors.Value.Parameters(CliVectors.G1));

        ProtocolParams objectParams = await koios.GetParametersAsync(timeout.Token);
        CborRationalNumber rate = Assert.IsType<CborRationalNumber>(objectParams.ExpansionRate);
        Assert.Equal(1UL, rate.Numerator);
        Assert.Equal(3UL, rate.Denominator);
    }

    [Fact]
    public async Task MetadataComesFromTheTransactionBytes()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        byte[] f04 = Vectors.Value.Bytes("cases/F04.tx");
        string f04Hex = Convert.ToHexStringLower(f04);
        string upperHash = new('B', 64);
        string lowerHash = new('b', 64);
        string? seenBody = null;
        using StubHttpHandler stub = new(async (request, ct) =>
        {
            seenBody = await request.Content!.ReadAsStringAsync(ct);
            return StubHttpHandler.Json($$"""[{"tx_hash":"{{lowerHash}}","cbor":"{{f04Hex}}"}]""");
        });
        using Koios koios = Create(stub);

        Metadata actual = Assert.IsType<Metadata>(await koios.GetTransactionMetadataAsync(upperHash, timeout.Token));
        Assert.Equal($$"""{"_tx_hashes":["{{upperHash}}"]}""", seenBody);

        Metadata expected = Assert.IsType<Metadata>(CborSerializer.Deserialize<PostMaryTransaction>(f04).AuxiliaryData?.Metadata());
        Assert.Equal(CborSerializer.Serialize(expected), CborSerializer.Serialize(actual));

        MetadatumMap label674 = Assert.IsType<MetadatumMap>(actual.Value[674]);
        MetadatumList msg = Assert.IsType<MetadatumList>(label674.Value.Single(p => p.Key is MetadataText { Value: "msg" }).Value);
        Assert.Equal("synthetic transfer", Assert.IsType<MetadataText>(Assert.Single(msg.Value)).Value);
        MetadatumMap nested = Assert.IsType<MetadatumMap>(label674.Value.Single(p => p.Key is MetadataText { Value: "nested" }).Value);
        Assert.Equal(3UL, Assert.IsType<MetadatumIntUlong>(nested.Value.Single(p => p.Key is MetadataText { Value: "counter" }).Value).Value);
    }

    [Fact]
    public async Task SubmitSendsTheCborOnce()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        byte[] expectedBytes = CborSerializer.Serialize(BlockfrostTests.Transaction);
        string expectedId = new('c', 64);
        string? seenContentType = null;
        byte[]? seenBytes = null;
        using StubHttpHandler okStub = new(async (request, ct) =>
        {
            seenContentType = request.Content!.Headers.ContentType?.MediaType;
            seenBytes = await request.Content.ReadAsByteArrayAsync(ct);
            return StubHttpHandler.Json($"\"{expectedId}\"", HttpStatusCode.Accepted);
        });
        using (Koios koios = Create(okStub))
        {
            Assert.Equal(expectedId, await koios.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
            Assert.Equal("application/cbor", seenContentType);
            Assert.Equal(expectedBytes, seenBytes);
        }

        const string canary = "CANARY-koios-submit";
        using StubHttpHandler badStub = new(_ => StubHttpHandler.Json(canary, HttpStatusCode.BadRequest));
        using (Koios koios = Create(badStub, token: canary))
        {
            ProviderStatusException status = await Assert.ThrowsAsync<ProviderStatusException>(() =>
                koios.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
            Assert.Equal(HttpStatusCode.BadRequest, status.StatusCode);
            Assert.False(status.IsTransient);
            Assert.Contains('*', status.Detail);
            ProviderHttpTests.AssertNoCanary(status, canary);
        }

        using StubHttpHandler ioStub = new(_ => throw new IOException("CANARY-lost-submit"));
        using (Koios koios = Create(ioStub))
        {
            ProviderException lost = await Assert.ThrowsAsync<ProviderException>(() =>
                koios.SubmitTransactionAsync(BlockfrostTests.Transaction, timeout.Token));
            Assert.True(lost.IsTransient);
            Assert.Contains("the transaction may have been accepted", lost.Message, StringComparison.Ordinal);
            Assert.Single(ioStub.Requests);
        }
    }

    [Theory]
    [InlineData("token", "CANARY7", "https://koios.test/api/v1/")]
    [InlineData("path", null, "https://koios.test/CANARY-path/")]
    [InlineData("query", null, "https://koios.test/api/v1/?token=CANARY-q")]
    [InlineData("password", null, "https://user:CANARY-pw@koios.test/api/v1/")]
    [InlineData("host", null, "https://CANARY-host.koios.test/api/v1/")]
    [InlineData("bad-newline", "CANARY-token\n", "https://koios.test/api/v1/")]
    [InlineData("bad-nonascii", "CANARY-€-tok", "https://koios.test/api/v1/")]
    [InlineData("no-token-null", null, "https://koios.test/api/v1/")]
    [InlineData("no-token-empty", "", "https://koios.test/api/v1/")]
    public async Task CredentialsNeverLeak(string scenario, string? token, string url)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        if (scenario is "bad-newline" or "bad-nonascii")
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() => { using Koios _ = new(NetworkType.Preprod, token, url); });
            Assert.DoesNotContain(token!, error.Message, StringComparison.Ordinal);
            return;
        }

        string secret = scenario switch
        {
            "token" => "CANARY7",
            "path" => "CANARY-path",
            "query" => "CANARY-q",
            "password" => "CANARY-pw",
            "host" => "CANARY-host",
            _ => ""
        };
        bool throwIo = false;
        string? seenAuth = null;
        using StubHttpHandler stub = new(request =>
        {
            seenAuth = request.Headers.Authorization?.ToString();
            return throwIo
                ? throw new IOException(secret)
                : StubHttpHandler.Json(secret.Length == 0 ? "Bearer token required" : secret, HttpStatusCode.Unauthorized);
        });
        using Koios koios = Create(stub, token, url);

        ProviderStatusException status = await Assert.ThrowsAsync<ProviderStatusException>(() => koios.GetParametersAsync(timeout.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        if (secret.Length == 0)
        {
            Assert.Equal("Bearer token required", status.Detail);
            Assert.Null(seenAuth);
            return;
        }
        if (scenario == "token") { Assert.Equal($"Bearer {token}", seenAuth); }
        Assert.Contains('*', status.Detail);
        ProviderHttpTests.AssertNoCanary(status, secret);

        throwIo = true;
        ProviderException transport = await Assert.ThrowsAsync<ProviderException>(() => koios.GetParametersAsync(timeout.Token));
        ProviderHttpTests.AssertNoCanary(transport, secret);
    }

    [Theory]
    [InlineData(NetworkType.Mainnet, "", "https://api.koios.rest/api/v1/cli_protocol_params")]
    [InlineData(NetworkType.Preview, "", "https://preview.koios.rest/api/v1/cli_protocol_params")]
    [InlineData(NetworkType.Preprod, "", "https://preprod.koios.rest/api/v1/cli_protocol_params")]
    [InlineData(NetworkType.Testnet, "", null)]
    [InlineData(NetworkType.Unknown, "", null)]
    [InlineData(NetworkType.Preprod, "https://koios.test/api/v1", "https://koios.test/api/v1/cli_protocol_params")]
    public async Task NetworkUrls(NetworkType network, string url, string? expectedUri)
    {
        if (expectedUri is null)
        {
            Assert.Throws<ArgumentException>(() => { using Koios _ = new(network, url: url); });
            return;
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string? seenUri = null;
        using StubHttpHandler stub = new(request =>
        {
            seenUri = request.RequestUri!.AbsoluteUri;
            return StubHttpHandler.Json(Encoding.UTF8.GetString(Vectors.Value.Bytes(CliVectors.G1)));
        });
        using Koios koios = new(network, url: url, handler: stub);
        _ = await koios.GetParametersAsync(timeout.Token);
        Assert.Equal(expectedUri, seenUri);
    }
}
