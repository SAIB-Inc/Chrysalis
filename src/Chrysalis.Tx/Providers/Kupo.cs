using System.Net.Http.Json;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Tx.Providers;

public class Kupo(string kupoEndpoint, NetworkType networkType = NetworkType.Preview) : ICardanoDataProvider
{
    private readonly HttpClient _httpClient = CreateHttpClient(kupoEndpoint);
    private readonly NetworkType _networkType = networkType;
    public NetworkType NetworkType => _networkType;

    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> addresses)
    {
        if (addresses.Count == 0) return [];

        // Query each address individually using the /matches/{address} endpoint
        IEnumerable<Task<List<KupoMatch>>> tasks = addresses.Select(FetchMatchesForAddress);
        List<KupoMatch>[] results = await Task.WhenAll(tasks);

        return [.. results.SelectMany(matches => matches.Select(ConvertToResolvedInput))];
    }

    public Task<ProtocolParams> GetParametersAsync()
    {
        ProtocolParametersResponse parameters = _networkType switch
        {
            NetworkType.Mainnet => PParamsDefaults.Mainnet(),
            NetworkType.Preview => PParamsDefaults.Preview(),
            NetworkType.Preprod => PParamsDefaults.Preview(), // Use Preview params for Preprod
            NetworkType.Testnet => PParamsDefaults.Preview(), // Use Preview params for Testnet
            _ => throw new ArgumentException($"Unsupported network type: {_networkType}")
        };

        Dictionary<int, CborDefList<long>> costMdls = [];

        foreach ((string key, int[] value) in parameters.CostModelsRaw ?? [])
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new ArgumentException("Invalid key", nameof(key))
            };

            costMdls[version] = new CborDefList<long>([.. value.Select(x => (long)x)]);
        }

        ProtocolParams protocolParams = new(
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
            new ProtocolVersion(9, 0),
            ulong.Parse(parameters.MinPoolCost),
            ulong.Parse(parameters.CoinsPerUtxoSize),
            new CostMdls(costMdls),
            new ExUnitPrices(
                new CborRationalNumber((ulong)(parameters.PriceMem * 1000000), 1000000),
                new CborRationalNumber((ulong)(parameters.PriceStep * 1000000), 1000000)
            ),
            new ExUnits(ulong.Parse(parameters.MaxTxExMem), ulong.Parse(parameters.MaxTxExSteps)),
            new ExUnits(ulong.Parse(parameters.MaxBlockExMem), ulong.Parse(parameters.MaxBlockExSteps)),
            ulong.Parse(parameters.MaxValSize),
            (ulong)parameters.CollateralPercent,
            (ulong)parameters.MaxCollateralInputs,
            new PoolVotingThresholds(
                new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1),
                new CborRationalNumber(1, 1), new CborRationalNumber(1, 1)
            ),
            new DRepVotingThresholds(
                new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1),
                new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1),
                new CborRationalNumber(1, 1), new CborRationalNumber(1, 1), new CborRationalNumber(1, 1),
                new CborRationalNumber(1, 1)
            ),
            1,
            1,
            1,
            1,
            1,
            1,
            new CborRationalNumber((ulong)parameters.MinFeeRefScriptCostPerByte, 1)
        );

        return Task.FromResult(protocolParams);
    }

    public Task<string> SubmitTransactionAsync(Transaction tx) =>
        throw new NotImplementedException(
            "Transaction submission is not supported by Kupo. " +
            "This operation requires Ogmios Local Tx Submit protocol.");

    public Task<Metadata?> GetTransactionMetadataAsync(string txHash) =>
        throw new NotImplementedException(
            "Transaction metadata retrieval by transaction hash is not supported by Kupo. " +
            "Kupo only provides metadata by slot number, which requires Ogmios to resolve " +
            "transaction hash to slot number.");

    private static HttpClient CreateHttpClient(string kupoEndpoint)
    {
        HttpClient client = new()
        {
            BaseAddress = new Uri(kupoEndpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(120)
        };

        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    private async Task<List<KupoMatch>> FetchMatchesForAddress(string address)
    {
        HttpResponseMessage response = await _httpClient.GetAsync($"matches/{address}?unspent&resolve_hashes");

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to fetch matches for {address}: {response.StatusCode} - {error}");
        }

        return await response.Content.ReadFromJsonAsync<List<KupoMatch>>() ?? [];
    }

    private static ResolvedInput ConvertToResolvedInput(KupoMatch match)
    {
        TransactionInput outref = CreateTransactionInput(match);
        Value value = CreateValue(match.Value);
        DatumOption? datum = CreateDatumOption(match);
        CborEncodedValue? scriptRef = CreateScriptReference(match.Script);
        Address address = CreateAddress(match.Address);

        PostAlonzoTransactionOutput output = new(address, value, datum, scriptRef);
        return new ResolvedInput(outref, output);
    }

    private static TransactionInput CreateTransactionInput(KupoMatch match) =>
        new(HexStringCache.FromHexString(match.TransactionId), (ulong)match.OutputIndex);

    private static Value CreateValue(KupoValue kupoValue)
    {
        Lovelace lovelace = new((ulong)kupoValue.Coins);

        return kupoValue.Assets is null or { Count: 0 }
            ? lovelace
            : new LovelaceWithMultiAsset(lovelace, CreateMultiAsset(kupoValue.Assets));
    }

    private static MultiAssetOutput CreateMultiAsset(Dictionary<string, long> assets)
    {
        Dictionary<byte[], TokenBundleOutput> assetDict = new(ByteArrayEqualityComparer.Instance);

        foreach ((string fullAssetName, long quantity) in assets)
        {
            // Asset name format: policyId.assetName
            string[] parts = fullAssetName.Split('.', 2);
            if (parts.Length != 2) continue;

            string policyId = parts[0];
            string assetName = parts[1];

            byte[] policy = HexStringCache.FromHexString(policyId);
            byte[] assetNameBytes = HexStringCache.FromHexString(assetName);

            if (assetDict.TryGetValue(policy, out TokenBundleOutput? existingBundle))
            {
                existingBundle.Value[assetNameBytes] = (ulong)quantity;
            }
            else
            {
                Dictionary<byte[], ulong> tokenBundle = new(ByteArrayEqualityComparer.Instance)
                {
                    [assetNameBytes] = (ulong)quantity
                };
                assetDict[policy] = new TokenBundleOutput(tokenBundle);
            }
        }

        return new MultiAssetOutput(assetDict);
    }

    private static DatumOption? CreateDatumOption(KupoMatch match)
    {
        if (!string.IsNullOrEmpty(match.Datum))
        {
            return match.DatumType switch
            {
                "inline" => new InlineDatumOption(1, new CborEncodedValue(HexStringCache.FromHexString(match.Datum))),
                "hash" => new DatumHashOption(0, HexStringCache.FromHexString(match.DatumHash!)),
                _ => null
            };
        }

        if (!string.IsNullOrEmpty(match.DatumHash))
        {
            return new DatumHashOption(0, HexStringCache.FromHexString(match.DatumHash));
        }

        return null;
    }

    private static CborEncodedValue? CreateScriptReference(KupoScript? script) =>
        !string.IsNullOrEmpty(script?.Script)
            ? new CborEncodedValue(HexStringCache.FromHexString(script.Script))
            : null;

    private static Address CreateAddress(string bech32Address) =>
        new(Wallet.Models.Addresses.Address.FromBech32(bech32Address).ToBytes());
}