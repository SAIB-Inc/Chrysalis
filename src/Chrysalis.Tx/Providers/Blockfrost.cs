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

    public Blockfrost(string apiKey, NetworkType networkType = NetworkType.Preview, string url = "")
    {
        _networkType = networkType;
        _httpClient = new()
        {
            BaseAddress = new Uri(string.IsNullOrEmpty(url) ? GetBaseUrl() : url)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("Project_id", apiKey);
    }

    public async Task<ProtocolParams> GetParametersAsync()
    {
        const string query = "epochs/latest/parameters";
        var response = await _httpClient.GetAsync(query);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GetParameters: HTTP error {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var parameters = JsonSerializer.Deserialize<BlockfrostProtocolParametersResponse>(content) ??
            throw new Exception("GetParameters: Could not parse response json");

        Dictionary<int, CborDefList<long>> costMdls = [];

        foreach (var (key, value) in parameters.CostModelsRaw ?? [])
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

        const int maxPageCount = 100; // Blockfrost limit
        var page = 1;
        var results = new List<ResolvedInput>();

        while (true)
        {
            var pagination = $"count={maxPageCount}&page={page}";
            var query = $"addresses/{address}/utxos?{pagination}";
            var response = await _httpClient.GetAsync($"{query}");


            var content = await response.Content.ReadAsStringAsync();
            var utxos = JsonSerializer.Deserialize<List<BlockfrostUtxo>>(content);

            if (utxos == null || utxos.Count == 0)
                break;
            foreach (var utxo in utxos)
            {
                ulong lovelace = 0;
                Dictionary<byte[], TokenBundleOutput> assets = [];
                foreach (var amount in utxo.Amount!)
                {
                    if (amount.Unit == "lovelace")
                    {
                        lovelace = ulong.Parse(amount.Quantity!);
                    }
                    else
                    {
                        byte[] policy = Convert.FromHexString(amount.Unit![..56]);
                        byte[] assetName = Convert.FromHexString(amount.Unit![56..]);
                        byte[]? existingKey = assets.Keys.FirstOrDefault(x => x.SequenceEqual(policy));
                        if (existingKey is null)
                        {
                            assets[policy] = new TokenBundleOutput(new Dictionary<byte[], ulong>
                            {
                                [assetName] = ulong.Parse(amount.Quantity!)
                            });
                        }
                        else
                        {
                            assets[existingKey].Value[assetName] = ulong.Parse(amount.Quantity!);
                        }
                    }
                }
                TransactionInput outref = new(Convert.FromHexString(utxo.TxHash!), (ulong)utxo.TxIndex!);
                Lovelace CborLovelace = new(lovelace);
                Value value = new Lovelace(lovelace);
                if (assets.Count > 0)
                {
                    value = new LovelaceWithMultiAsset(CborLovelace, new MultiAssetOutput(assets));
                }

                ChrysalisWallet.Address outputAddress = ChrysalisWallet.Address.FromBech32(utxo.Address!);
                CborEncodedValue? scriptRef = null;

                if (utxo.ReferenceScriptHash is not null)
                {
                    Script scriptRefValue = await GetScript(utxo.ReferenceScriptHash);
                    scriptRef = new CborEncodedValue(CborSerializer.Serialize(scriptRefValue));
                }

                DatumOption? datum = null;
                if (utxo.InlineDatum is not null)
                {
                    datum = new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString(utxo.InlineDatum)));
                }

                TransactionOutput output = new PostAlonzoTransactionOutput(
                    new Address(outputAddress.ToBytes())
                    , value, datum, scriptRef);


                results.Add(new ResolvedInput(outref, output));
            }


            if (utxos.Count < maxPageCount)
                break;

            page++;

        }
        return results;
    }

    public async Task<Script> GetScript(string scriptHash)
    {
        var typeQuery = $"scripts/{scriptHash}";
        var typeResponse = await _httpClient.GetAsync($"{typeQuery}");
        var typeContent = await typeResponse.Content.ReadAsStringAsync();

        using var typeDoc = JsonDocument.Parse(typeContent);
        var root = typeDoc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        var type = typeElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse type from response");

        if (type == "timelock")
        {
            throw new Exception("GetScriptRef: Native scripts are not yet supported.");
        }

        var cborQuery = $"scripts/{scriptHash}/cbor";
        var cborResponse = await _httpClient.GetAsync($"{cborQuery}");
        var cborContent = await cborResponse.Content.ReadAsStringAsync();

        using var cborDoc = JsonDocument.Parse(cborContent);
        root = cborDoc.RootElement;

        if (!root.TryGetProperty("cbor", out var cborElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        var cborHex = cborElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse CBOR from response");

        byte[] cborBytes = Convert.FromHexString(cborHex);
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
        var tasks = addresses.Select(GetUtxosAsync);
        var results = await Task.WhenAll(tasks);
        return [.. results.SelectMany(utxos => utxos)];
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
        var query = $"txs/{txHash}/metadata";
        var response = await _httpClient.GetAsync(query);

        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            throw new Exception($"GetTransactionMetadata: HTTP error {response.StatusCode} - Response: {content}");
        }

        var rawMetadata = JsonSerializer.Deserialize<List<BlockfrostMetadataResponse>>(content);
        
        if (rawMetadata == null || rawMetadata.Count == 0)
            return null;

        var metadataDict = new Dictionary<ulong, TransactionMetadatum>();
        foreach (var item in rawMetadata)
        {
            if (ulong.TryParse(item.Label, out var label))
            {
                var metadatum = ConvertToTransactionMetadatum(item.JsonMetadata);
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
            JsonValueKind.Number when element.TryGetInt64(out var lng) => new MetadatumIntLong(lng),
            JsonValueKind.Number when element.TryGetUInt64(out var ulng) => new MetadatumIntUlong(ulng),
            JsonValueKind.Object => new MetadatumMap(
                element.EnumerateObject().ToDictionary(
                    prop => new MetadataText(prop.Name) as TransactionMetadatum,
                    prop => ConvertJsonElementToMetadatum(prop.Value) ?? new MetadataText("")
                )
            ),
            JsonValueKind.Array => new MetadatumList(
                element.EnumerateArray()
                    .Select(ConvertJsonElementToMetadatum)
                    .Where(m => m != null)
                    .Cast<TransactionMetadatum>()
                    .ToList()
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
}
