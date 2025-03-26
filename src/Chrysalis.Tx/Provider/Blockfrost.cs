using System.Net.Http.Headers;
using System.Text.Json;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using TxAddr = Chrysalis.Tx.Models.Addresses;

namespace Chrysalis.Tx.Provider;

public class Blockfrost : IProvider
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

        Dictionary<int, CborIndefList<long>> costMdls = [];

        foreach (var (key, value) in parameters.CostModelsRaw)
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new ArgumentException("Invalid key", nameof(key))
            };

            costMdls[version] = new CborIndefList<long>([.. value.Select(x => x)]);
        }

        return new ConwayProtocolParamUpdate(
            (ulong)parameters.MinFeeA,
            (ulong)parameters.MinFeeB,
            (ulong)parameters.MaxBlockSize,
            (ulong)parameters.MaxTxSize,
            (ulong)parameters.MaxBlockHeaderSize,
            ulong.Parse(parameters.KeyDeposit),
            ulong.Parse(parameters.PoolDeposit),
            (ulong)parameters.EMax,
            (ulong)parameters.NOpt,
            new CborRationalNumber((ulong)(parameters.A0 * 100), 100),
            new CborRationalNumber((ulong)(parameters.Rho * 100), 100),
            new CborRationalNumber((ulong)(parameters.Tau * 100), 100),
            ulong.Parse(parameters.MinPoolCost),
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

                TxAddr.Address outputAddress = TxAddr.Address.FromBech32(utxo.Address!);
                CborEncodedValue? scriptRef = null;

                if(utxo.ReferenceScriptHash is not null)
                {
                    ScriptRef scriptRefValue = await GetScript(utxo.ReferenceScriptHash);
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

    public async Task<ScriptRef> GetScript(string scriptHash)
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

        var type = typeElement.GetString();
        if (type == null)
        {
            throw new Exception("GetScriptRef: Could not parse type from response");
        }

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

        var cborHex = cborElement.GetString();
        if (cborHex == null)
        {
            throw new Exception("GetScriptRef: Could not parse CBOR from response");
        }

        byte[] cborBytes = Convert.FromHexString(cborHex);
        int scriptType = type switch
        {
            "plutusV1" => 1,
            "plutusV2" => 2,
            "plutusV3" => 3,
            _ => throw new Exception("GetScriptRef: Unsupported script type")
        };

        return new ScriptRef(scriptType, cborBytes);
    }

    private string GetBaseUrl()
    {
        //TODO: implement network specific base url
        return "https://cardano-preview.blockfrost.io/api/v0";
    }



}
