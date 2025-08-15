using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Tx.Providers;

public record BlockfrostMetadataResponse(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("json_metadata")] object JsonMetadata);

public class Blockfrost : ICardanoDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly NetworkType _networkType;

    public NetworkType NetworkType => _networkType;

    private readonly ConcurrentDictionary<string, Script> _scriptCache = new();

    public Blockfrost(string apiKey, NetworkType networkType = NetworkType.Preview, string url = "")
    {
        _networkType = networkType;

        HttpClientHandler handler = new();

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(string.IsNullOrEmpty(url) ? GetBaseUrl() : url),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("Project_id", apiKey);
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    public async Task<ProtocolParams> GetParametersAsync()
    {
        const string query = "epochs/latest/parameters";
        HttpResponseMessage response = await _httpClient.GetAsync(query);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GetParameters: HTTP error {response.StatusCode}");
        }

        string content = await response.Content.ReadAsStringAsync();
        BlockfrostProtocolParametersResponse parameters = JsonSerializer.Deserialize<BlockfrostProtocolParametersResponse>(content) ??
            throw new Exception("GetParameters: Could not parse response json");

        Dictionary<int, CborMaybeIndefList<long>> costMdls = [];

        foreach ((string key, int[] value) in parameters.CostModelsRaw ?? [])
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new ArgumentException("Invalid key", nameof(key))
            };

            costMdls[version] = new CborDefList<long>([.. value.Select(x => x)]);
        }

        return new ProtocolParams(
            (ulong)(parameters.MinFeeA ?? 0),
            (ulong)(parameters.MinFeeB ?? 0),
            (ulong)(parameters.MaxBlockSize ?? 0),
            (ulong)(parameters.MaxTxSize ?? 0),
            (ulong)(parameters.MaxBlockHeaderSize ?? 0),
            ulong.Parse(parameters.KeyDeposit ?? "0"),
            ulong.Parse(parameters.PoolDeposit ?? "0"),
            (ulong)(parameters.EMax ?? 0),
            (ulong)(parameters.NOpt ?? 0),
            new CborRationalNumber((ulong)((parameters.A0 ?? 0) * 100), 100),
            new CborRationalNumber((ulong)((parameters.Rho ?? 0) * 100), 100),
            new CborRationalNumber((ulong)((parameters.Tau ?? 0) * 100), 100),
            new ProtocolVersion(9, 0),
            ulong.Parse(parameters.MinPoolCost ?? "0"),
            ulong.Parse(parameters.CoinsPerUtxoSize),
            new CostMdls(costMdls),
            new ExUnitPrices(new CborRationalNumber((ulong)(parameters.PriceMem * 1000000), 1000000), new CborRationalNumber((ulong)(parameters.PriceStep * 1000000), 1000000)),
            new ExUnits(ulong.Parse(parameters.MaxTxExMem), ulong.Parse(parameters.MaxTxExSteps)),
            new ExUnits(ulong.Parse(parameters.MaxBlockExMem), ulong.Parse(parameters.MaxBlockExSteps)),
            ulong.Parse(parameters.MaxValSize),
            (ulong)parameters.CollateralPercent,
            (ulong)parameters.MaxCollateralInputs,
            new PoolVotingThresholds(new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1)),
            new DRepVotingThresholds(new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1)),
            1,
            1,
            1,
            1,
            1,
            1,
            new CborRationalNumber((ulong)parameters.MinFeeRefScriptCostPerByte!, 1)
        );
    }

    public async Task<List<ResolvedInput>> GetUtxosAsync(string address)
    {
        const int maxPageCount = 100;
        int page = 1;
        List<ResolvedInput> results = [];

        while (true)
        {
            string pagination = $"count={maxPageCount}&page={page}";
            string query = $"addresses/{address}/utxos?{pagination}";
            HttpResponseMessage response = await _httpClient.GetAsync($"{query}");

            string content = await response.Content.ReadAsStringAsync();
            List<BlockfrostUtxo>? utxos = JsonSerializer.Deserialize<List<BlockfrostUtxo>>(content);

            if (utxos == null || utxos.Count == 0)
                break;

            Task<ResolvedInput>[] batchTasks = [.. utxos.Select(ProcessUtxo)];
            ResolvedInput[] batchResults = await Task.WhenAll(batchTasks);
            results.AddRange(batchResults);

            if (utxos.Count < maxPageCount)
                break;

            page++;
        }
        return results;
    }

    private async Task<ResolvedInput> ProcessUtxo(BlockfrostUtxo utxo)
    {
        ulong lovelace = 0;

        Dictionary<byte[], TokenBundleOutput> assets = new(ByteArrayEqualityComparer.Instance);

        foreach (Amount amount in utxo.Amount!)
        {
            if (amount.Unit == "lovelace")
            {
                lovelace = ulong.Parse(amount.Quantity!);
            }
            else
            {
                byte[] policy = HexStringCache.FromHexString(amount.Unit![..56]);
                byte[] assetName = HexStringCache.FromHexString(amount.Unit![56..]);

                if (assets.TryGetValue(policy, out TokenBundleOutput? existingBundle))
                {
                    existingBundle.Value[assetName] = ulong.Parse(amount.Quantity!);
                }
                else
                {
                    assets[policy] = new TokenBundleOutput(new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance)
                    {
                        [assetName] = ulong.Parse(amount.Quantity!)
                    });
                }
            }
        }

        TransactionInput outref = new(HexStringCache.FromHexString(utxo.TxHash!), (ulong)utxo.TxIndex!);
        Lovelace cborLovelace = new(lovelace);
        Value value = assets.Count > 0
            ? new LovelaceWithMultiAsset(cborLovelace, new MultiAssetOutput(assets))
            : cborLovelace;

        ChrysalisWallet.Address outputAddress = ChrysalisWallet.Address.FromBech32(utxo.Address!);

        CborEncodedValue? scriptRef = null;
        if (utxo.ReferenceScriptHash is not null)
        {
            Script scriptRefValue = await GetScriptCached(utxo.ReferenceScriptHash);
            scriptRef = new CborEncodedValue(CborSerializer.Serialize(scriptRefValue));
        }

        DatumOption? datum = null;
        if (utxo.InlineDatum is not null)
        {
            datum = new InlineDatumOption(1, new CborEncodedValue(HexStringCache.FromHexString(utxo.InlineDatum)));
        }

        TransactionOutput output = new PostAlonzoTransactionOutput(
            new Address(outputAddress.ToBytes()),
            value,
            datum,
            scriptRef);

        return new ResolvedInput(outref, output);
    }

    private async Task<Script> GetScriptCached(string scriptHash)
    {
        if (_scriptCache.TryGetValue(scriptHash, out Script? cachedScript))
            return cachedScript;

        Script script = await GetScript(scriptHash);
        _scriptCache.TryAdd(scriptHash, script);
        return script;
    }

    public async Task<Script> GetScript(string scriptHash)
    {
        string typeQuery = $"scripts/{scriptHash}";
        HttpResponseMessage typeResponse = await _httpClient.GetAsync($"{typeQuery}");
        string typeContent = await typeResponse.Content.ReadAsStringAsync();

        using JsonDocument typeDoc = JsonDocument.Parse(typeContent);
        JsonElement root = typeDoc.RootElement;

        if (!root.TryGetProperty("type", out JsonElement typeElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        string type = typeElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse type from response");

        if (type == "timelock")
        {
            throw new Exception("GetScriptRef: Native scripts are not yet supported.");
        }

        string cborQuery = $"scripts/{scriptHash}/cbor";
        HttpResponseMessage cborResponse = await _httpClient.GetAsync($"{cborQuery}");
        string cborContent = await cborResponse.Content.ReadAsStringAsync();

        using JsonDocument cborDoc = JsonDocument.Parse(cborContent);
        root = cborDoc.RootElement;

        if (!root.TryGetProperty("cbor", out JsonElement cborElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        string cborHex = cborElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse CBOR from response");

        byte[] cborBytes = HexStringCache.FromHexString(cborHex);
        Script script = type switch
        {
            "plutusV1" => new PlutusV1Script(new Value1(1), cborBytes),
            "plutusV2" => new PlutusV2Script(new Value2(2), cborBytes),
            "plutusV3" => new PlutusV3Script(new Value3(3), cborBytes),
            _ => throw new Exception("GetScriptRef: Unsupported script type")
        };

        return script;
    }

    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> addresses)
    {
        const int batchSize = 5;
        List<ResolvedInput> allResults = [];

        for (int i = 0; i < addresses.Count; i += batchSize)
        {
            IEnumerable<string> batch = addresses.Skip(i).Take(batchSize);
            IEnumerable<Task<List<ResolvedInput>>> batchTasks = batch.Select(GetUtxosAsync);
            List<ResolvedInput>[] batchResults = await Task.WhenAll(batchTasks);

            allResults.AddRange(batchResults.SelectMany(utxos => utxos));

            if (i + batchSize < addresses.Count)
            {
                await Task.Delay(50);
            }
        }

        return allResults;
    }

    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        string query = "tx/submit";

        ByteArrayContent content = new(CborSerializer.Serialize(tx));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/cbor");

        HttpResponseMessage response = await _httpClient.PostAsync(query, content);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            throw new Exception($"SubmitTransactionAsync: failed to submit transaction to Blockfrost endpoint.\nError {error}");
        }

        string txId = await response.Content.ReadAsStringAsync();
        txId = JsonSerializer.Deserialize<string>(txId) ??
            throw new Exception("SubmitTransactionAsync: Could not parse transaction ID from response");

        return txId;
    }

    public async Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        string query = $"txs/{txHash}/metadata";
        HttpResponseMessage response = await _httpClient.GetAsync(query);

        string content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            throw new Exception($"GetTransactionMetadata: HTTP error {response.StatusCode} - Response: {content}");
        }

        List<BlockfrostMetadataResponse>? rawMetadata = JsonSerializer.Deserialize<List<BlockfrostMetadataResponse>>(content);

        if (rawMetadata == null || rawMetadata.Count == 0)
            return null;

        Dictionary<ulong, TransactionMetadatum> metadataDict = [];
        foreach (BlockfrostMetadataResponse item in rawMetadata)
        {
            if (ulong.TryParse(item.Label, out ulong label))
            {
                TransactionMetadatum? metadatum = ConvertToTransactionMetadatum(item.JsonMetadata);
                if (metadatum != null)
                {
                    metadataDict[label] = metadatum;
                }
            }
        }

        return metadataDict.Count > 0 ? new Metadata(metadataDict) : null;
    }

    private static TransactionMetadatum? ConvertToTransactionMetadatum(object? value)
    {
        return value switch
        {
            string str => new MetadataText(str),
            long lng => new MetadatumIntLong(lng),
            int i => new MetadatumIntLong(i),
            JsonElement element => ConvertJsonElementToMetadatum(element),
            Dictionary<string, object> dict => new MetadatumMap(
                dict.ToDictionary(
                    kv => ConvertToTransactionMetadatum(kv.Key) ?? new MetadataText(kv.Key),
                    kv => ConvertToTransactionMetadatum(kv.Value) ?? new MetadataText(kv.Value?.ToString() ?? "")
                )
            ),
            _ => value?.ToString() is string s ? new MetadataText(s) : null
        };
    }

    private static TransactionMetadatum? ConvertJsonElementToMetadatum(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => new MetadataText(element.GetString() ?? ""),
            JsonValueKind.Number when element.TryGetInt64(out long lng) => new MetadatumIntLong(lng),
            JsonValueKind.Number when element.TryGetUInt64(out ulong ulng) => new MetadatumIntUlong(ulng),
            JsonValueKind.Object => new MetadatumMap(
                element.EnumerateObject().ToDictionary(
                    prop => new MetadataText(prop.Name) as TransactionMetadatum,
                    prop => ConvertJsonElementToMetadatum(prop.Value) ?? new MetadataText("")
                )
            ),
            JsonValueKind.Array => new MetadatumList(
                [.. element.EnumerateArray()
                    .Select(ConvertJsonElementToMetadatum)
                    .Where(m => m != null)
                    .Cast<TransactionMetadatum>()]
            ),
            _ => new MetadataText(element.ToString())
        };
    }

    private string GetBaseUrl()
    {
        return _networkType switch
        {
            NetworkType.Mainnet => "https://cardano-mainnet.blockfrost.io/api/v0/",
            NetworkType.Preview => "https://cardano-preview.blockfrost.io/api/v0/",
            NetworkType.Preprod => "https://cardano-preprod.blockfrost.io/api/v0/",
            NetworkType.Testnet => "https://cardano-testnet.blockfrost.io/api/v0/",
            _ => throw new ArgumentException($"Unsupported network type: {_networkType}")
        };
    }

    public (int ScriptCacheSize, (int BytesToHex, int HexToBytes) HexCacheStats) GetCacheStats()
    {
        return (_scriptCache.Count, HexStringCache.GetCacheStats());
    }
}