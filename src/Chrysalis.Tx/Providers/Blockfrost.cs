using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Utils;
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

/// <summary>
/// Represents a metadata response item from the Blockfrost API.
/// </summary>
/// <param name="Label">The metadata label.</param>
/// <param name="JsonMetadata">The metadata JSON value.</param>
public record BlockfrostMetadataResponse(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("json_metadata")] object JsonMetadata);

/// <summary>
/// Blockfrost-based implementation of the Cardano data provider.
/// </summary>
public sealed class Blockfrost : ICardanoDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler _handler;

    /// <summary>
    /// Gets the network type for this provider.
    /// </summary>
    public NetworkType NetworkType { get; }

    private readonly ConcurrentDictionary<string, Script> _scriptCache = new();

    /// <summary>
    /// Initializes a new Blockfrost provider with the given API key and network.
    /// </summary>
    /// <param name="apiKey">The Blockfrost project API key.</param>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="url">Optional custom API base URL.</param>
    public Blockfrost(string apiKey, NetworkType networkType = NetworkType.Preview, string url = "")
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        NetworkType = networkType;

        _handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true
        };

        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri(string.IsNullOrEmpty(url) ? GetBaseUrl() : url),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("Project_id", apiKey);
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    /// <summary>
    /// Retrieves the current protocol parameters from Blockfrost.
    /// </summary>
    /// <returns>The current protocol parameters.</returns>
    public async Task<ProtocolParams> GetParametersAsync()
    {
        const string query = "epochs/latest/parameters";
        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(query, UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetParameters: HTTP error {response.StatusCode}");
        }

        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        BlockfrostProtocolParametersResponse parameters = JsonSerializer.Deserialize<BlockfrostProtocolParametersResponse>(content) ??
            throw new InvalidOperationException("GetParameters: Could not parse response json");

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

        return new ProtocolParams(
            (ulong)(parameters.MinFeeA ?? 0),
            (ulong)(parameters.MinFeeB ?? 0),
            (ulong)(parameters.MaxBlockSize ?? 0),
            (ulong)(parameters.MaxTxSize ?? 0),
            (ulong)(parameters.MaxBlockHeaderSize ?? 0),
            ulong.Parse(parameters.KeyDeposit ?? "0", CultureInfo.InvariantCulture),
            ulong.Parse(parameters.PoolDeposit ?? "0", CultureInfo.InvariantCulture),
            (ulong)(parameters.EMax ?? 0),
            (ulong)(parameters.NOpt ?? 0),
            new CborRationalNumber((ulong)((parameters.A0 ?? 0) * 100), 100),
            new CborRationalNumber((ulong)((parameters.Rho ?? 0) * 100), 100),
            new CborRationalNumber((ulong)((parameters.Tau ?? 0) * 100), 100),
            new ProtocolVersion(9, 0),
            ulong.Parse(parameters.MinPoolCost ?? "0", CultureInfo.InvariantCulture),
            ulong.Parse(parameters.CoinsPerUtxoSize, CultureInfo.InvariantCulture),
            new CostMdls(costMdls),
            new ExUnitPrices(new CborRationalNumber((ulong)(parameters.PriceMem * 1000000), 1000000), new CborRationalNumber((ulong)(parameters.PriceStep * 1000000), 1000000)),
            new ExUnits(ulong.Parse(parameters.MaxTxExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxTxExSteps, CultureInfo.InvariantCulture)),
            new ExUnits(ulong.Parse(parameters.MaxBlockExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxBlockExSteps, CultureInfo.InvariantCulture)),
            ulong.Parse(parameters.MaxValSize, CultureInfo.InvariantCulture),
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

    /// <summary>
    /// Retrieves UTxOs for a single address.
    /// </summary>
    /// <param name="address">The Bech32 address.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        const int maxPageCount = 100;
        int page = 1;
        List<ResolvedInput> results = [];

        while (true)
        {
            string pagination = string.Create(CultureInfo.InvariantCulture, $"count={maxPageCount}&page={page}");
            string query = $"addresses/{address}/utxos?{pagination}";
            using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(query, UriKind.Relative)).ConfigureAwait(false);

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            List<BlockfrostUtxo>? utxos = JsonSerializer.Deserialize<List<BlockfrostUtxo>>(content);

            if (utxos == null || utxos.Count == 0)
            {
                break;
            }

            Task<ResolvedInput>[] batchTasks = [.. utxos.Select(ProcessUtxo)];
            ResolvedInput[] batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);
            results.AddRange(batchResults);

            if (utxos.Count < maxPageCount)
            {
                break;
            }

            page++;
        }
        return results;
    }

    private async Task<ResolvedInput> ProcessUtxo(BlockfrostUtxo utxo)
    {
        ulong lovelace = 0;

        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> assets = new(ReadOnlyMemoryComparer.Instance);

        foreach (Amount amount in utxo.Amount!)
        {
            if (string.Equals(amount.Unit, "lovelace", StringComparison.Ordinal))
            {
                lovelace = ulong.Parse(amount.Quantity!, CultureInfo.InvariantCulture);
            }
            else
            {
                ReadOnlyMemory<byte> policy = HexStringCache.FromHexString(amount.Unit![..56]);
                ReadOnlyMemory<byte> assetName = HexStringCache.FromHexString(amount.Unit![56..]);

                if (assets.TryGetValue(policy, out TokenBundleOutput? existingBundle))
                {
                    existingBundle.Value[assetName] = ulong.Parse(amount.Quantity!, CultureInfo.InvariantCulture);
                }
                else
                {
                    assets[policy] = new TokenBundleOutput(new Dictionary<ReadOnlyMemory<byte>, ulong>(ReadOnlyMemoryComparer.Instance)
                    {
                        [assetName] = ulong.Parse(amount.Quantity!, CultureInfo.InvariantCulture)
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
            Script scriptRefValue = await GetScriptCached(utxo.ReferenceScriptHash).ConfigureAwait(false);
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
        {
            return cachedScript;
        }

        Script script = await GetScript(scriptHash).ConfigureAwait(false);
        _ = _scriptCache.TryAdd(scriptHash, script);
        return script;
    }

    /// <summary>
    /// Retrieves a script by its hash from Blockfrost.
    /// </summary>
    /// <param name="scriptHash">The script hash.</param>
    /// <returns>The script object.</returns>
    public async Task<Script> GetScript(string scriptHash)
    {
        ArgumentNullException.ThrowIfNull(scriptHash);

        string typeQuery = $"scripts/{scriptHash}";
        using HttpResponseMessage typeResponse = await _httpClient.GetAsync(new Uri(typeQuery, UriKind.Relative)).ConfigureAwait(false);
        string typeContent = await typeResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using JsonDocument typeDoc = JsonDocument.Parse(typeContent);
        JsonElement root = typeDoc.RootElement;

        if (!root.TryGetProperty("type", out JsonElement typeElement))
        {
            throw new InvalidOperationException("GetScriptRef: Could not parse response json");
        }

        string type = typeElement.GetString() ?? throw new InvalidOperationException("GetScriptRef: Could not parse type from response");

        if (string.Equals(type, "timelock", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GetScriptRef: Native scripts are not yet supported.");
        }

        string cborQuery = $"scripts/{scriptHash}/cbor";
        using HttpResponseMessage cborResponse = await _httpClient.GetAsync(new Uri(cborQuery, UriKind.Relative)).ConfigureAwait(false);
        string cborContent = await cborResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        using JsonDocument cborDoc = JsonDocument.Parse(cborContent);
        JsonElement cborRoot = cborDoc.RootElement;

        if (!cborRoot.TryGetProperty("cbor", out JsonElement cborElement))
        {
            throw new InvalidOperationException("GetScriptRef: Could not parse response json");
        }

        string cborHex = cborElement.GetString() ?? throw new InvalidOperationException("GetScriptRef: Could not parse CBOR from response");

        byte[] cborBytes = HexStringCache.FromHexString(cborHex);
        Script script = type switch
        {
            "plutusV1" => new PlutusV1Script(1, cborBytes),
            "plutusV2" => new PlutusV2Script(2, cborBytes),
            "plutusV3" => new PlutusV3Script(3, cborBytes),
            _ => throw new InvalidOperationException($"GetScriptRef: Unsupported script type: {type}")
        };

        return script;
    }

    /// <summary>
    /// Retrieves UTxOs for multiple addresses.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address)
    {
        ArgumentNullException.ThrowIfNull(address);

        const int batchSize = 5;
        List<ResolvedInput> allResults = [];

        for (int i = 0; i < address.Count; i += batchSize)
        {
            IEnumerable<string> batch = address.Skip(i).Take(batchSize);
            IEnumerable<Task<List<ResolvedInput>>> batchTasks = batch.Select(GetUtxosAsync);
            List<ResolvedInput>[] batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

            allResults.AddRange(batchResults.SelectMany(utxos => utxos));

            if (i + batchSize < address.Count)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        return allResults;
    }

    /// <summary>
    /// Submits a signed transaction to the Blockfrost API.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        using ByteArrayContent content = new(CborSerializer.Serialize(tx));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/cbor");

        using HttpResponseMessage response = await _httpClient.PostAsync(new Uri("tx/submit", UriKind.Relative), content).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"SubmitTransactionAsync: failed to submit transaction to Blockfrost endpoint.\nError {error}");
        }

        string txId = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        txId = JsonSerializer.Deserialize<string>(txId) ??
            throw new InvalidOperationException("SubmitTransactionAsync: Could not parse transaction ID from response");

        return txId;
    }

    /// <summary>
    /// Retrieves transaction metadata by transaction hash.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <returns>The metadata, or null if not found.</returns>
    public async Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        ArgumentNullException.ThrowIfNull(txHash);

        string query = $"txs/{txHash}/metadata";
        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(query, UriKind.Relative)).ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? null
                : throw new InvalidOperationException($"GetTransactionMetadata: HTTP error {response.StatusCode} - Response: {content}");
        }

        List<BlockfrostMetadataResponse>? rawMetadata = JsonSerializer.Deserialize<List<BlockfrostMetadataResponse>>(content);

        if (rawMetadata == null || rawMetadata.Count == 0)
        {
            return null;
        }

        Dictionary<ulong, TransactionMetadatum> metadataDict = [];
        foreach (BlockfrostMetadataResponse item in rawMetadata)
        {
            if (ulong.TryParse(item.Label, CultureInfo.InvariantCulture, out ulong label))
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

    private static TransactionMetadatum? ConvertToTransactionMetadatum(object value)
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
            _ => value.ToString() is string s ? new MetadataText(s) : null
        };
    }

    private static TransactionMetadatum? ConvertJsonElementToMetadatum(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => new MetadataText(element.GetString() ?? ""),
            JsonValueKind.Number when element.TryGetInt64(out long lng) => new MetadatumIntLong(lng),
            JsonValueKind.Number when element.TryGetUInt64(out _) => new MetadatumIntUlong(element.GetUInt64()),
            JsonValueKind.Number => new MetadataText(element.ToString()),
            JsonValueKind.Object => new MetadatumMap(
                element.EnumerateObject().ToDictionary(
                    prop => (TransactionMetadatum)new MetadataText(prop.Name),
                    prop => ConvertJsonElementToMetadatum(prop.Value) ?? new MetadataText("")
                )
            ),
            JsonValueKind.Array => new MetadatumList(
                [.. element.EnumerateArray()
                    .Select(ConvertJsonElementToMetadatum)
                    .Where(m => m != null)
                    .Cast<TransactionMetadatum>()]
            ),
            JsonValueKind.Undefined => new MetadataText(element.ToString()),
            JsonValueKind.Null => new MetadataText(element.ToString()),
            JsonValueKind.True => new MetadataText(element.ToString()),
            JsonValueKind.False => new MetadataText(element.ToString()),
            _ => new MetadataText(element.ToString())
        };
    }

    private string GetBaseUrl()
    {
        return NetworkType switch
        {
            NetworkType.Mainnet => "https://cardano-mainnet.blockfrost.io/api/v0/",
            NetworkType.Preview => "https://cardano-preview.blockfrost.io/api/v0/",
            NetworkType.Preprod => "https://cardano-preprod.blockfrost.io/api/v0/",
            NetworkType.Testnet => "https://cardano-testnet.blockfrost.io/api/v0/",
            NetworkType.Unknown => throw new NotImplementedException(),
            _ => throw new ArgumentException($"Unsupported network type: {NetworkType}")
        };
    }

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    public (int ScriptCacheSize, (int BytesToHex, int HexToBytes) HexCacheStats) CacheStats =>
        (_scriptCache.Count, HexStringCache.CacheStats);

    /// <summary>
    /// Disposes the underlying HTTP client and handler.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }
}
