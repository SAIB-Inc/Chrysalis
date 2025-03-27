using System.Net.Http.Headers;
using System.Text.Json;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
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

    public async Task<ConwayProtocolParamUpdate> GetProtocolParametersAsync()
    {
        string query = "/epochs/latest/parameters";
        HttpResponseMessage response = await _httpClient.GetAsync($"{_baseUrl}{query}");

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GetParameters: HTTP error {response.StatusCode}");
        }

        string content = await response.Content.ReadAsStringAsync();
        BlockfrostProtocolParametersResponse parameters = JsonSerializer.Deserialize<BlockfrostProtocolParametersResponse>(content) ??
            throw new Exception("GetParameters: Could not parse response json");

        Dictionary<int, CborIndefList<long>> costMdls = [];

        foreach (var (key, value) in parameters.CostModelsRaw ?? [])
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

    public async Task<List<ResolvedInput>> GetUtxosByAddressAsync(List<string> bech32Address)
    {
        List<ResolvedInput> results = [];
        foreach (var address in bech32Address)
        {
            var utxos = await GetUtxosByAddressAsync(address);
            results.AddRange(utxos);
        }
        return results;
    }

    public async Task<List<ResolvedInput>> GetUtxosByAddressAsync(string address)
    {

        int maxPageCount = 100;
        int page = 1;
        List<ResolvedInput> results = [];

        while (true)
        {
            string pagination = $"count={maxPageCount}&page={page}";
            string query = $"/addresses/{address}/utxos?{pagination}";
            HttpResponseMessage response = await _httpClient.GetAsync($"{_baseUrl}{query}");


            string content = await response.Content.ReadAsStringAsync();
            List<BlockfrostUtxo> utxos = JsonSerializer.Deserialize<List<BlockfrostUtxo>>(content) ?? [];

            if (utxos == null || utxos.Count == 0)
                break;
            foreach (BlockfrostUtxo utxo in utxos)
            {
                ulong lovelace = 0;
                Dictionary<byte[], TokenBundleOutput> assets = [];
                foreach (Amount amount in utxo.Amount!)
                {
                    if (amount.Unit == "lovelace")
                    {
                        lovelace = ulong.Parse(amount.Quantity!);
                    }
                    else
                    {
                        byte[] policy = Convert.FromHexString(amount.Unit![..56]);
                        byte[] assetName = Convert.FromHexString(amount.Unit![56..]);
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

    public Task<List<ResolvedInput>> GetUtxosByTxIns(List<TransactionInput> outrefs)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
    
    public async Task<ScriptRef> GetScript(string scriptHash)
    {
        string typeQuery = $"/scripts/{scriptHash}";
        HttpResponseMessage typeResponse = await _httpClient.GetAsync($"{_baseUrl}{typeQuery}");
        string typeContent = await typeResponse.Content.ReadAsStringAsync();

        using JsonDocument typeDoc = JsonDocument.Parse(typeContent);
        JsonElement root = typeDoc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        var type = typeElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse type from response");

        if (type == "timelock")
        {
            throw new Exception("GetScriptRef: Native scripts are not yet supported.");
        }

        string cborQuery = $"/scripts/{scriptHash}/cbor";
        HttpResponseMessage cborResponse = await _httpClient.GetAsync($"{_baseUrl}{cborQuery}");
        string cborContent = await cborResponse.Content.ReadAsStringAsync();

        using JsonDocument cborDoc = JsonDocument.Parse(cborContent);
        root = cborDoc.RootElement;

        if (!root.TryGetProperty("cbor", out var cborElement))
        {
            throw new Exception("GetScriptRef: Could not parse response json");
        }

        string cborHex = cborElement.GetString() ?? throw new Exception("GetScriptRef: Could not parse CBOR from response");

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
