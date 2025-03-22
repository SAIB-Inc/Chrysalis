using System.Net.Http.Headers;
using System.Text.Json;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models;

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

        Dictionary<CborInt, CborDefList<CborLong>> costMdls = new();

        foreach (var (key, value) in parameters.CostModelsRaw)
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new ArgumentException("Invalid key", nameof(key))
            };

            costMdls[new CborInt(version)] = new CborDefList<CborLong>([.. value.Select(x => new CborLong(x))]);
        }

        return new ConwayProtocolParamUpdate(
            new CborUlong((ulong)parameters.MinFeeA),
            new CborUlong((ulong)parameters.MinFeeB),
            new CborUlong((ulong)parameters.MaxBlockSize),
            new CborUlong((ulong)parameters.MaxTxSize),
            new CborUlong((ulong)parameters.MaxBlockHeaderSize),
            new CborUlong(ulong.Parse(parameters.KeyDeposit)),
            new CborUlong(ulong.Parse(parameters.PoolDeposit)),
            new CborUlong((ulong)parameters.EMax),
            new CborUlong((ulong)parameters.NOpt),
            new CborRationalNumber((ulong)(parameters.A0 * 100), 100),
            new CborRationalNumber((ulong)(parameters.Rho * 100), 100),
            new CborRationalNumber((ulong)(parameters.Tau * 100), 100),
            new CborUlong(ulong.Parse(parameters.MinPoolCost)),
            new CborUlong(ulong.Parse(parameters.CoinsPerUtxoSize)),
            new CostMdls(costMdls),
            new ExUnitPrices(new CborRationalNumber((ulong)(parameters.PriceMem * 1000000), 1000000), new CborRationalNumber((ulong)(parameters.PriceStep * 1000000), 1000000)),
            new ExUnits(new CborUlong(ulong.Parse(parameters.MaxTxExMem)), new CborUlong(ulong.Parse(parameters.MaxTxExSteps))),
            new ExUnits(new CborUlong(ulong.Parse(parameters.MaxBlockExMem)), new CborUlong(ulong.Parse(parameters.MaxBlockExSteps))),
            new CborUlong(ulong.Parse(parameters.MaxValSize)),
            new CborUlong((ulong)parameters.CollateralPercent),
            new CborUlong((ulong)parameters.MaxCollateralInputs),
            new PoolVotingThresholds(new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1)), 
            new DRepVotingThresholds(new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1,1 ), new CborRationalNumber(1, 1)),
            new CborUlong(1),
            new CborUlong(1),
            new CborUlong(1),
            new CborUlong(1),
            new CborUlong(1),
            new CborUlong(1),
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
                Dictionary<CborBytes, TokenBundleOutput> assets = [];
                foreach (var amount in utxo.Amount!)
                {
                    if (amount.Unit == "lovelace")
                    {
                        lovelace = ulong.Parse(amount.Quantity!);
                    }
                    else
                    {
                        var policy = new CborBytes(Convert.FromHexString(amount.Unit![..56]));
                        var assetName = new CborBytes(Convert.FromHexString(amount.Unit![56..]));
                        if (!assets.ContainsKey(policy))
                        {
                            assets[policy] = new TokenBundleOutput(new Dictionary<CborBytes, CborUlong>
                            {
                                [assetName] = new CborUlong(ulong.Parse(amount.Quantity!))
                            });
                        }
                        else
                        {
                            assets[policy].Value[assetName] = new CborUlong(ulong.Parse(amount.Quantity!));
                        }
                    }
                }
                TransactionInput outref = new(new CborBytes(Convert.FromHexString(utxo.TxHash!)), new CborUlong((ulong)utxo.TxIndex!));
                Lovelace CborLovelace = new(lovelace);
                Value value = new Lovelace(lovelace);
                if (assets.Count > 0)
                {
                    value = new LovelaceWithMultiAsset(CborLovelace, new MultiAssetOutput(assets));
                }

                TransactionOutput output = new PostAlonzoTransactionOutput(
                    // Address Utility is not yet implemented so hardcoded for now
                    new Address(Convert.FromHexString("005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba"))
                    , value, null, null);


                results.Add(new ResolvedInput(outref, output));
            }


            if (utxos.Count < maxPageCount)
                break;

            page++;

        }
        return results;
    }

    private string GetBaseUrl()
    {
        //TODO: implement network specific base url
        return "https://cardano-preview.blockfrost.io/api/v0";
    }



}
