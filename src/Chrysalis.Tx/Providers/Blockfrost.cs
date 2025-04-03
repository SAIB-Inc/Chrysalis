using System.Net.Http.Headers;
using System.Text.Json;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Tx.Providers;

public class Blockfrost : ICardanoDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public Blockfrost(string apiKey)
    {
        _httpClient = new HttpClient();
        _baseUrl = GetBaseUrl();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("project_id", apiKey);
    }

    public async Task<ConwayProtocolParamUpdate> GetParametersAsync()
    {
        const string query = "/epochs/latest/parameters";
        var response = await _httpClient.GetAsync($"{_baseUrl}{query}");

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

        return new ConwayProtocolParamUpdate(
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
            var query = $"/addresses/{address}/utxos?{pagination}";
            var response = await _httpClient.GetAsync($"{_baseUrl}{query}");


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
                        var policy = Convert.FromHexString(amount.Unit![..56]);
                        var assetName = Convert.FromHexString(amount.Unit![56..]);
                        if (!assets.ContainsKey(policy))
                        {
                            assets[policy] = new TokenBundleOutput(new Dictionary<byte[], ulong>
                            {
                                [assetName] = ulong.Parse(amount.Quantity!)
                            });
                        }
                        else
                        {
                            assets[policy].Value[assetName] = ulong.Parse(amount.Quantity!);
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
                    // Address Utility is not yet implemented so hardcoded for now
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
        var typeQuery = $"/scripts/{scriptHash}";
        var typeResponse = await _httpClient.GetAsync($"{_baseUrl}{typeQuery}");
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

        var cborQuery = $"/scripts/{scriptHash}/cbor";
        var cborResponse = await _httpClient.GetAsync($"{_baseUrl}{cborQuery}");
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
        string query = _baseUrl + "/tx/submit";

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
    private string GetBaseUrl()
    {
        //TODO: implement network specific base url
        return "https://cardano-preview.blockfrost.io/api/v0";
    }

}
