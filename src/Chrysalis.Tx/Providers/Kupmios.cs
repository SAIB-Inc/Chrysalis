using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chrysalis.Cbor.Serialization.Utils;
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
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Kupo+Ogmios (Kupmios) implementation of the Cardano data provider.
/// </summary>
public sealed class Kupmios : ICardanoDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _ogmiosClient;

    /// <summary>
    /// Gets the network type for this provider.
    /// </summary>
    public NetworkType NetworkType { get; }

    /// <summary>
    /// Initializes a new Kupmios provider.
    /// </summary>
    /// <param name="kupoEndpoint">The Kupo API endpoint URL.</param>
    /// <param name="ogmiosEndpoint">The Ogmios API endpoint URL.</param>
    /// <param name="networkType">The Cardano network type.</param>
    public Kupmios(string kupoEndpoint, string ogmiosEndpoint, NetworkType networkType = NetworkType.Preview)
    {
        ArgumentNullException.ThrowIfNull(kupoEndpoint);
        ArgumentNullException.ThrowIfNull(ogmiosEndpoint);

        _httpClient = CreateHttpClient(kupoEndpoint);
        _ogmiosClient = new HttpClient() { BaseAddress = new Uri(ogmiosEndpoint), Timeout = TimeSpan.FromSeconds(30) };
        NetworkType = networkType;
    }

    /// <summary>
    /// Retrieves UTxOs for multiple addresses via Kupo.
    /// </summary>
    /// <param name="address">The list of addresses or patterns.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Count == 0)
        {
            return [];
        }

        // Query each address individually using the /matches/{address} endpoint
        IEnumerable<Task<List<KupoMatch>>> tasks = address.Select(FetchMatchesForAddress);
        List<KupoMatch>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return [.. results.SelectMany(matches => matches.Select(ConvertToResolvedInput))];
    }

    /// <summary>
    /// Retrieves UTxOs by payment key hash pattern.
    /// </summary>
    /// <param name="paymentKey">The payment key hash.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosByPaymentKeyAsync(string paymentKey)
    {
        ArgumentNullException.ThrowIfNull(paymentKey);

        string pattern = paymentKey.EndsWith("/*", StringComparison.Ordinal)
            ? paymentKey
            : $"{paymentKey}/*";

        return GetUtxosAsync([pattern]);
    }

    /// <summary>
    /// Retrieves UTxOs by multiple payment key hash patterns.
    /// </summary>
    /// <param name="paymentKeys">The payment key hashes.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosByPaymentKeysAsync(List<string> paymentKeys)
    {
        ArgumentNullException.ThrowIfNull(paymentKeys);

        if (paymentKeys.Count == 0)
        {
            return Task.FromResult<List<ResolvedInput>>([]);
        }

        List<string> patterns = [.. paymentKeys.Select(key => key.EndsWith("/*", StringComparison.Ordinal) ? key : $"{key}/*")];

        return GetUtxosAsync(patterns);
    }

    /// <summary>
    /// Retrieves a UTxO by its output reference (transaction hash + index).
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="outputIndex">The output index.</param>
    /// <returns>The resolved input, or null if not found.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong outputIndex)
    {
        ArgumentNullException.ThrowIfNull(txHash);

        string pattern = string.Create(CultureInfo.InvariantCulture, $"{outputIndex}@{txHash}");

        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri($"matches/{pattern}?unspent&resolve_hashes", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to fetch UTXO for {pattern}: {response.StatusCode} - {error}");
        }

        List<KupoMatch>? matches = await response.Content.ReadFromJsonAsync<List<KupoMatch>>().ConfigureAwait(false);

        return matches is null || matches.Count == 0 ? null : ConvertToResolvedInput(matches[0]);
    }

    /// <summary>
    /// Retrieves a UTxO by its TransactionInput reference.
    /// </summary>
    /// <param name="outRef">The transaction input reference.</param>
    /// <returns>The resolved input, or null if not found.</returns>
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(TransactionInput outRef)
    {
        ArgumentNullException.ThrowIfNull(outRef);
        return GetUtxoByOutRefAsync(Convert.ToHexString(outRef.TransactionId.Span), outRef.Index);
    }

    /// <summary>
    /// Retrieves UTxOs by multiple output references.
    /// </summary>
    /// <param name="outRefs">The output references as tuples.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosByOutRefsAsync(List<(string TxHash, ulong OutputIndex)> outRefs)
    {
        ArgumentNullException.ThrowIfNull(outRefs);

        if (outRefs.Count == 0)
        {
            return [];
        }

        IEnumerable<Task<ResolvedInput?>> tasks = outRefs.Select(outRef => GetUtxoByOutRefAsync(outRef.TxHash, outRef.OutputIndex));
        ResolvedInput?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return [.. results.Where(r => r is not null).Select(r => r!)];
    }

    /// <summary>
    /// Retrieves UTxOs by multiple TransactionInput references.
    /// </summary>
    /// <param name="outRefs">The transaction input references.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosByOutRefsAsync(List<TransactionInput> outRefs)
    {
        ArgumentNullException.ThrowIfNull(outRefs);

        if (outRefs.Count == 0)
        {
            return [];
        }

        IEnumerable<Task<ResolvedInput?>> tasks = outRefs.Select(GetUtxoByOutRefAsync);
        ResolvedInput?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return [.. results.Where(r => r is not null).Select(r => r!)];
    }

    /// <summary>
    /// Retrieves protocol parameters (uses defaults for each network).
    /// </summary>
    /// <returns>The protocol parameters.</returns>
    public Task<ProtocolParams> GetParametersAsync()
    {
        ProtocolParametersResponse parameters = NetworkType switch
        {
            NetworkType.Mainnet => PParamsDefaults.Mainnet(),
            NetworkType.Preview => PParamsDefaults.Preview(),
            NetworkType.Preprod => PParamsDefaults.Preview(),
            NetworkType.Testnet => PParamsDefaults.Preview(),
            NetworkType.Unknown => throw new NotImplementedException(),
            _ => throw new ArgumentException($"Unsupported network type: {NetworkType}")
        };

        Dictionary<int, CborMaybeIndefList<long>> costMdls = [];

        foreach ((string key, int[] value) in parameters.CostModelsRaw ?? [])
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new ArgumentException($"Invalid cost model key: {key}", nameof(key))
            };

            costMdls[version] = new CborDefList<long>([.. value.Select(x => (long)x)]);
        }

        ProtocolParams protocolParams = new(
            (ulong)parameters.MinFeeA,
            (ulong)parameters.MinFeeB,
            (ulong)parameters.MaxBlockSize,
            (ulong)parameters.MaxTxSize,
            (ulong)parameters.MaxBlockHeaderSize,
            ulong.Parse(parameters.KeyDeposit, CultureInfo.InvariantCulture),
            ulong.Parse(parameters.PoolDeposit, CultureInfo.InvariantCulture),
            (ulong)parameters.EMax,
            (ulong)parameters.NOpt,
            new CborRationalNumber((ulong)(parameters.A0 * 100), 100),
            new CborRationalNumber((ulong)(parameters.Rho * 100), 100),
            new CborRationalNumber((ulong)(parameters.Tau * 100), 100),
            new ProtocolVersion(9, 0),
            ulong.Parse(parameters.MinPoolCost, CultureInfo.InvariantCulture),
            ulong.Parse(parameters.CoinsPerUtxoSize, CultureInfo.InvariantCulture),
            new CostMdls(costMdls),
            new ExUnitPrices(
                new CborRationalNumber((ulong)(parameters.PriceMem * 1000000), 1000000),
                new CborRationalNumber((ulong)(parameters.PriceStep * 1000000), 1000000)
            ),
            new ExUnits(ulong.Parse(parameters.MaxTxExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxTxExSteps, CultureInfo.InvariantCulture)),
            new ExUnits(ulong.Parse(parameters.MaxBlockExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxBlockExSteps, CultureInfo.InvariantCulture)),
            ulong.Parse(parameters.MaxValSize, CultureInfo.InvariantCulture),
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

    /// <summary>
    /// Submits a signed transaction via Ogmios.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        byte[] txBytes = CborSerializer.Serialize(tx);
        string txCbor = Convert.ToHexString(txBytes).ToUpperInvariant();

        var request = new
        {
            jsonrpc = "2.0",
            method = "submitTransaction",
            @params = new { transaction = new { cbor = txCbor } },
            id = Guid.NewGuid().ToString()
        };

        string json = JsonSerializer.Serialize(request);
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _ogmiosClient.PostAsync((Uri?)null, content).ConfigureAwait(false);
        string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(responseJson);

        if (result.TryGetProperty("error", out JsonElement error))
        {
            string message = error.GetProperty("message").GetString() ?? "Unknown error";
            throw new InvalidOperationException($"Ogmios submit failed: {message}");
        }

        return result.TryGetProperty("result", out JsonElement res) &&
            res.TryGetProperty("transaction", out JsonElement txResult) &&
            txResult.TryGetProperty("id", out JsonElement txId)
            ? txId.GetString() ?? throw new InvalidOperationException("Could not parse transaction ID")
            : throw new InvalidOperationException("Unexpected Ogmios response format");
    }

    /// <summary>
    /// Transaction metadata retrieval is not supported by Kupo.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <returns>Always throws NotImplementedException.</returns>
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        throw new NotImplementedException(
            "Transaction metadata retrieval by transaction hash is not supported by Kupo. " +
            "Kupo only provides metadata by slot number, which requires Ogmios to resolve " +
            "transaction hash to slot number.");
    }

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
        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri($"matches/{address}?unspent&resolve_hashes", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to fetch matches for {address}: {response.StatusCode} - {error}");
        }

        return await response.Content.ReadFromJsonAsync<List<KupoMatch>>().ConfigureAwait(false) ?? [];
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

    private static TransactionInput CreateTransactionInput(KupoMatch match)
    {
        return new(HexStringCache.FromHexString(match.TransactionId), (ulong)match.OutputIndex);
    }

    private static Value CreateValue(KupoValue kupoValue)
    {
        Lovelace lovelace = new((ulong)kupoValue.Coins);

        return kupoValue.Assets is null or { Count: 0 }
            ? lovelace
            : new LovelaceWithMultiAsset(lovelace, CreateMultiAsset(kupoValue.Assets));
    }

    private static MultiAssetOutput CreateMultiAsset(Dictionary<string, long> assets)
    {
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assetDict = new(ReadOnlyMemoryComparer.Instance);

        foreach ((string fullAssetName, long quantity) in assets)
        {
            string[] parts = fullAssetName.Split('.', 2);
            string policyId = parts[0];
            string assetName = parts.Length > 1 ? parts[1] : string.Empty;

            ReadOnlyMemory<byte> policy = HexStringCache.FromHexString(policyId);
            ReadOnlyMemory<byte> assetNameBytes = HexStringCache.FromHexString(assetName);

            if (assetDict.TryGetValue(policy, out TokenBundleOutput? existingBundle))
            {
                existingBundle.Value[assetNameBytes] = (ulong)quantity;
            }
            else
            {
                Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = new(ReadOnlyMemoryComparer.Instance)
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
        return !string.IsNullOrEmpty(match.Datum)
            ? match.DatumType switch
            {
                "inline" => new InlineDatumOption(1, new CborEncodedValue(HexStringCache.FromHexString(match.Datum))),
                "hash" => new DatumHashOption(0, HexStringCache.FromHexString(match.DatumHash!)),
                _ => null
            }
            : !string.IsNullOrEmpty(match.DatumHash) ? new DatumHashOption(0, HexStringCache.FromHexString(match.DatumHash)) : null;
    }

    private static CborEncodedValue? CreateScriptReference(KupoScript? script)
    {
        if (string.IsNullOrEmpty(script?.Script) || string.IsNullOrEmpty(script?.Language))
        {
            return null;
        }

        byte[] scriptBytes = HexStringCache.FromHexString(script.Script);

        Script scriptObj = script.Language switch
        {
            "native" => new MultiSigScript(0, CborSerializer.Deserialize<NativeScript>(scriptBytes)),
            "plutus:v1" => new PlutusV1Script(1, scriptBytes),
            "plutus:v2" => new PlutusV2Script(2, scriptBytes),
            "plutus:v3" => new PlutusV3Script(3, scriptBytes),
            _ => throw new NotSupportedException($"Unsupported script language: {script.Language}")
        };

        return new CborEncodedValue(CborSerializer.Serialize(scriptObj));
    }

    private static Address CreateAddress(string bech32Address)
    {
        return new(Wallet.Models.Addresses.Address.FromBech32(bech32Address).ToBytes());
    }

    /// <summary>
    /// Disposes the underlying HTTP clients.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        _ogmiosClient.Dispose();
    }
}
