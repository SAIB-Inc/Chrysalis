using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Types.Cardano.Core;
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
/// Blockfrost data provider. Protocol version is fixed at 9.0 and governance thresholds
/// are placeholders; by-output-reference reads do not check whether the output is spent.
/// </summary>
public sealed class Blockfrost : ICardanoDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler? _handler;
    private readonly ProviderHttp _http;
    private const string Provider = nameof(Blockfrost);

    /// <summary>
    /// Gets the network type for this provider.
    /// </summary>
    public NetworkType NetworkType { get; }

    private readonly ConcurrentDictionary<string, IScript> _scriptCache = new();

    /// <summary>
    /// Initializes a new Blockfrost provider with the given API key and network.
    /// </summary>
    /// <param name="apiKey">The Blockfrost project API key.</param>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="url">Optional custom API base URL.</param>
    /// <param name="handler">Optional caller-owned HTTP handler.</param>
    public Blockfrost(string apiKey, NetworkType networkType = NetworkType.Preview, string url = "", HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        NetworkType = networkType;

        _handler = handler is null ? new HttpClientHandler
        {
            CheckCertificateRevocationList = true
        } : null;

        _httpClient = new HttpClient(handler ?? _handler!, disposeHandler: false)
        {
            BaseAddress = new Uri(string.IsNullOrEmpty(url) ? GetBaseUrl() : url),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("Project_id", apiKey);
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        _http = new(_httpClient, Provider, [apiKey, .. ProviderHttp.SecretsOf(_httpClient.BaseAddress!)]);
    }

    /// <summary>
    /// Retrieves the current protocol parameters from Blockfrost.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current protocol parameters.</returns>
    public async Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetParametersAsync);
        BlockfrostProtocolParametersResponse parameters = (await _http.GetJsonAsync<BlockfrostProtocolParametersResponse>(
            "epochs/latest/parameters", operation, null, cancellationToken).ConfigureAwait(false))!;
        return ProviderHttp.Map(parameters, MapParameters, Provider, operation);
    }

    private static ProtocolParams MapParameters(BlockfrostProtocolParametersResponse parameters)
    {
        Dictionary<int, CborList<long>> costMdls = [];

        foreach ((string key, int[] value) in parameters.CostModelsRaw ?? [])
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new InvalidOperationException("Unknown cost model key.")
            };

            costMdls[version] = CborList.Create(value.Select(x => (long)x));
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
            CborRationalNumber.Create((ulong)((parameters.A0 ?? 0) * 100), 100),
            CborRationalNumber.Create((ulong)((parameters.Rho ?? 0) * 100), 100),
            CborRationalNumber.Create((ulong)((parameters.Tau ?? 0) * 100), 100),
            ProtocolVersion.Create(9, 0),
            ulong.Parse(parameters.MinPoolCost ?? "0", CultureInfo.InvariantCulture),
            ulong.Parse(parameters.CoinsPerUtxoSize, CultureInfo.InvariantCulture),
            new CostMdls(costMdls.GetValueOrDefault(0), costMdls.GetValueOrDefault(1), costMdls.GetValueOrDefault(2)),
            // Preserve the existing 1e7 scale: price_step 0.0000721 is 721/10_000_000.
            // Deserialize prices directly as decimal so a binary floating-point approximation
            // cannot move a halfway value across the intended round-to-even boundary.
            ExUnitPrices.Create(
                CborRationalNumber.Create((ulong)Math.Round(parameters.PriceMem * 10_000_000m, MidpointRounding.ToEven), 10_000_000),
                CborRationalNumber.Create((ulong)Math.Round(parameters.PriceStep * 10_000_000m, MidpointRounding.ToEven), 10_000_000)),
            ExUnits.Create(ulong.Parse(parameters.MaxTxExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxTxExSteps, CultureInfo.InvariantCulture)),
            ExUnits.Create(ulong.Parse(parameters.MaxBlockExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxBlockExSteps, CultureInfo.InvariantCulture)),
            ulong.Parse(parameters.MaxValSize, CultureInfo.InvariantCulture),
            (ulong)parameters.CollateralPercent,
            (ulong)parameters.MaxCollateralInputs,
            PoolVotingThresholds.Create(CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1)),
            DRepVotingThresholds.Create(CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1)),
            1,
            1,
            1,
            1,
            1,
            1,
            CborRationalNumber.Create((ulong)parameters.MinFeeRefScriptCostPerByte!, 1)
        );
    }

    /// <summary>
    /// Retrieves UTxOs for a single address.
    /// </summary>
    /// <param name="address">The Bech32 address.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        cancellationToken.ThrowIfCancellationRequested();
        const int pageSize = 100;
        const string operation = nameof(GetUtxosAsync);
        ResolvedInputSet found = new(Provider, operation);
        await ProviderHttp.ReadAllPagesAsync(ReadPage, 1, pageSize, AddPage, Provider, operation, cancellationToken).ConfigureAwait(false);
        return found.ToList();

        async Task<List<BlockfrostUtxo>> ReadPage(int page, CancellationToken ct) =>
            await _http.GetJsonAsync<List<BlockfrostUtxo>>(
                string.Create(CultureInfo.InvariantCulture, $"addresses/{Uri.EscapeDataString(address)}/utxos?count={pageSize}&page={page}"),
                operation, page == 1 ? IsNotFoundBody : null, ct).ConfigureAwait(false) ?? [];

        async Task AddPage(List<BlockfrostUtxo> page, CancellationToken ct)
        {
            foreach (BlockfrostUtxo? utxo in page)
            {
                if (utxo?.TxIndex is not >= 0) { throw ProviderHttp.Unusable(Provider, operation, "missing or invalid output index"); }
                found.Add(address, await ProcessUtxoAsync(utxo, (ulong)utxo.TxIndex.Value, operation, ct).ConfigureAwait(false));
            }
        }
    }

    private async Task<ResolvedInput> ProcessUtxoAsync(BlockfrostUtxo utxo, ulong index, string operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IScript? script = utxo.ReferenceScriptHash is { } hash
            ? await GetScriptCachedAsync(hash, cancellationToken).ConfigureAwait(false) : null;
        return ProviderHttp.Map(utxo, item => MapOutput(item, index, script, operation), Provider, operation);
    }

    private static ResolvedInput MapOutput(BlockfrostUtxo utxo, ulong index, IScript? script, string operation)
    {
        if (utxo.TxHash is not { Length: 64 } hash || string.IsNullOrEmpty(utxo.Address) || utxo.Amount is null)
        {
            throw ProviderHttp.Unusable(Provider, operation, "missing or invalid output fields");
        }
        ulong? lovelace = null;
        List<(ReadOnlyMemory<byte> Policy, ReadOnlyMemory<byte> Name, ulong Quantity)> assets = [];
        foreach (Amount? amount in utxo.Amount)
        {
            if (amount?.Unit is not { } unit || !ulong.TryParse(amount.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out ulong quantity))
            {
                throw ProviderHttp.Unusable(Provider, operation, "missing unit or invalid quantity");
            }
            if (string.Equals(unit, "lovelace", StringComparison.Ordinal))
            {
                if (lovelace.HasValue) { throw ProviderHttp.Unusable(Provider, operation, "repeated lovelace entry"); }
                lovelace = quantity;
            }
            else
            {
                if (unit.Length < 56) { throw ProviderHttp.Unusable(Provider, operation, "invalid asset unit"); }
                assets.Add((HexStringCache.FromHexString(unit[..56]), HexStringCache.FromHexString(unit[56..]), quantity));
            }
        }
        IValue value = ProviderValues.Create(lovelace ?? throw ProviderHttp.Unusable(Provider, operation, "missing lovelace"), assets, Provider, operation);
        TransactionInput outref = TransactionInput.Create(HexStringCache.FromHexString(hash), index);
        IDatumOption? datum = utxo.InlineDatum is { } inline
            ? InlineDatumOption.Create(1, new CborEncodedValue(HexStringCache.FromHexString(inline))) : null;
        ITransactionOutput output = PostAlonzoTransactionOutput.Create(new Address(ChrysalisWallet.Address.FromBech32(utxo.Address).ToBytes()),
            value, datum, script is null ? null : new CborEncodedValue(CborSerializer.Serialize(script)));
        return new(outref, output);
    }

    private async Task<IScript> GetScriptCachedAsync(string scriptHash, CancellationToken cancellationToken)
    {
        if (_scriptCache.TryGetValue(scriptHash, out IScript? script)) { return script; }
        script = await GetScript(scriptHash, cancellationToken).ConfigureAwait(false);
        _ = _scriptCache.TryAdd(scriptHash, script);
        return script;
    }

    /// <summary>
    /// Retrieves a script by its hash from Blockfrost.
    /// </summary>
    /// <param name="scriptHash">The script hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The script object.</returns>
    public async Task<IScript> GetScript(string scriptHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetScript);
        string path = $"scripts/{Uri.EscapeDataString(scriptHash)}";
        using JsonDocument descriptor = (await _http.GetJsonAsync<JsonDocument>(path, operation, null, cancellationToken).ConfigureAwait(false))!;
        string? language = ProviderHttp.Map(descriptor.RootElement, element => element.GetProperty("type").GetString(), Provider, operation);
        if (language == "timelock") { throw ProviderHttp.Unusable(Provider, operation, "native reference scripts are not mapped yet"); }
        if (language is not ("plutusV1" or "plutusV2" or "plutusV3"))
        {
            throw ProviderHttp.Unusable(Provider, operation, "unknown script language");
        }
        using JsonDocument body = (await _http.GetJsonAsync<JsonDocument>($"{path}/cbor", operation, null, cancellationToken).ConfigureAwait(false))!;
        return ProviderHttp.Map<JsonElement, IScript>(body.RootElement, element =>
        {
            byte[] bytes = HexStringCache.FromHexString(element.GetProperty("cbor").GetString()
                ?? throw ProviderHttp.Unusable(Provider, operation, "missing script bytes"));
            return language switch
            {
                "plutusV1" => PlutusV1Script.Create(1, bytes),
                "plutusV2" => PlutusV2Script.Create(2, bytes),
                _ => PlutusV3Script.Create(3, bytes)
            };
        }, Provider, operation);
    }

    /// <summary>
    /// Retrieves UTxOs for multiple addresses.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        foreach (string item in address) { ArgumentException.ThrowIfNullOrWhiteSpace(item, nameof(address)); }
        cancellationToken.ThrowIfCancellationRequested();
        List<string> requests = [.. address.Distinct(StringComparer.Ordinal)];
        ResolvedInputSet merged = new(Provider, nameof(GetUtxosAsync));
        const int batchSize = 5;
        for (int i = 0; i < requests.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] batch = [.. requests.Skip(i).Take(batchSize)];
            using CancellationTokenSource batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            List<ResolvedInput>[] answers = await ProviderHttp.AwaitBatchAsync(
                Task.WhenAll(batch.Select(ReadAddressAsync)), cancellationToken).ConfigureAwait(false);

            async Task<List<ResolvedInput>> ReadAddressAsync(string item)
            {
                try { return await GetUtxosAsync(item, batchCancellation.Token).ConfigureAwait(false); }
                catch (ProviderException)
                {
                    try { await batchCancellation.CancelAsync().ConfigureAwait(false); }
                    catch (AggregateException) { /* A cancellation callback must not replace the provider failure. */ }
                    throw;
                }
            }
            for (int j = 0; j < batch.Length; j++)
            {
                foreach (ResolvedInput input in answers[j]) { merged.Add(batch[j], input); }
            }
            if (i + batchSize < requests.Count) { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return merged.ToList();
    }

    /// <summary>
    /// Retrieves a specific UTxO by its output reference.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The matching output or null.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetUtxoByOutRefAsync);
        using JsonDocument? body = await _http.GetJsonAsync<JsonDocument>($"txs/{Uri.EscapeDataString(txHash)}/utxos",
            operation, IsNotFoundBody, cancellationToken).ConfigureAwait(false);
        if (body is null) { return null; }
        BlockfrostUtxo? utxo = ProviderHttp.Map(body.RootElement, root => FindOutput(root, txHash, index), Provider, operation);
        return utxo is null ? null : await ProcessUtxoAsync(utxo, index, operation, cancellationToken).ConfigureAwait(false);
    }

    private static BlockfrostUtxo? FindOutput(JsonElement root, string txHash, ulong index)
    {
        BlockfrostUtxo? match = null;
        foreach (JsonElement item in root.GetProperty("outputs").EnumerateArray())
        {
            if (item.GetProperty("output_index").GetUInt64() != index) { continue; }
            if (match is not null) { throw ProviderHttp.Unusable(Provider, nameof(GetUtxoByOutRefAsync), "repeated output"); }
            match = item.Deserialize<BlockfrostUtxo>() ?? throw ProviderHttp.Unusable(Provider, nameof(GetUtxoByOutRefAsync), "null output");
            match.TxHash = txHash;
        }
        return match;
    }

    private static bool IsNotFoundBody(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status_code", out JsonElement status) &&
                status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out int code) && code == (int)HttpStatusCode.NotFound &&
                root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String &&
                error.ValueEquals("Not Found") &&
                root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Submits a signed transaction to the Blockfrost API.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(SubmitTransactionAsync);
        using HttpRequestMessage request = CreateTransactionRequest("tx/submit", tx);
        bool receivedSuccess = false;
        try
        {
            using HttpResponseMessage response = (await _http.SendAsync(request, operation, null, cancellationToken).ConfigureAwait(false))!;
            receivedSuccess = true;
            return await _http.ReadJsonAsync<string>(response, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException error) when (error is not ProviderStatusException && (receivedSuccess || error.IsTransient))
        {
            throw new ProviderException($"{error.Message}; the transaction may have been accepted", error.IsTransient, error.Detail);
        }
    }

    private static HttpRequestMessage CreateTransactionRequest(string path, ITransaction tx) => new(HttpMethod.Post, path)
    {
        Content = new ByteArrayContent(CborSerializer.Serialize(tx)) { Headers = { ContentType = new MediaTypeHeaderValue("application/cbor") } }
    };

    /// <summary>
    /// Evaluates a transaction using the Blockfrost/Ogmios evaluate endpoint.
    /// Returns execution units for each redeemer as computed by the Cardano node.
    /// </summary>
    /// <param name="tx">The unsigned transaction to evaluate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A dictionary mapping (tag, index) to (mem, steps) execution units.</returns>
    public async Task<Dictionary<(int Tag, ulong Index), (ulong Mem, ulong Steps)>> EvaluateTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(EvaluateTransactionAsync);
        using HttpRequestMessage request = CreateTransactionRequest("utils/txs/evaluate", tx);
        using HttpResponseMessage response = (await _http.SendAsync(request, operation, null, cancellationToken).ConfigureAwait(false))!;
        using JsonDocument document = await _http.ReadJsonAsync<JsonDocument>(response, operation, cancellationToken).ConfigureAwait(false);
        return ProviderHttp.Map(document.RootElement, MapEvaluation, Provider, operation);
    }

    private Dictionary<(int Tag, ulong Index), (ulong Mem, ulong Steps)> MapEvaluation(JsonElement root)
    {
        const string operation = nameof(EvaluateTransactionAsync);
        Dictionary<(int, ulong), (ulong, ulong)> results = [];

        // Parse Ogmios EvaluateTx response format
        if (root.TryGetProperty("result", out JsonElement result))
        {
            if (result.TryGetProperty("EvaluationResult", out JsonElement evalResult))
            {
                foreach (JsonProperty prop in evalResult.EnumerateObject())
                {
                    // Key format: "spend:0", "mint:0", etc.
                    string[] parts = prop.Name.Split(':', 2);
                    if (parts.Length != 2) { throw ProviderHttp.Unusable(Provider, operation, "invalid evaluation key"); }
                    int tag = parts[0] switch
                    {
                        "spend" => 0,
                        "mint" => 1,
                        "cert" => 2,
                        "reward" => 3,
                        "vote" => 4,
                        "propose" => 5,
                        _ => -1
                    };

                    if (tag < 0 || !ulong.TryParse(parts[1], CultureInfo.InvariantCulture, out ulong index))
                    {
                        throw ProviderHttp.Unusable(Provider, operation, "invalid evaluation key");
                    }
                    // Ogmios returns {memory, steps} directly (no "budget" wrapper).
                    ulong mem = prop.Value.GetProperty("memory").GetUInt64();
                    ulong steps = prop.Value.GetProperty("steps").GetUInt64();
                    if (!results.TryAdd((tag, index), (mem, steps))) { throw ProviderHttp.Unusable(Provider, operation, "duplicate evaluation key"); }
                }
            }
            else if (result.TryGetProperty("EvaluationFailure", out JsonElement failure))
            {
                throw new ProviderException($"{Provider} {operation}: evaluation rejected", isTransient: false,
                    _http.RedactDetail(failure.GetRawText()));
            }
        }

        if (results.Count == 0)
        {
            throw new ProviderException($"{Provider} {operation}: unusable response: no evaluation results", isTransient: false,
                _http.RedactDetail(root.GetRawText()));
        }

        return results;
    }

    /// <summary>
    /// Creates an evaluator function that uses the Blockfrost evaluate endpoint.
    /// Pass the returned function to <see cref="Builders.TxBuilder.UseEvaluator"/> or
    /// <see cref="Builders.TransactionTemplateBuilder{T}.UseEvaluator"/>.
    /// </summary>
    public Func<PostMaryTransaction, IReadOnlyList<ResolvedInput>, Task<IReadOnlyList<Plutus.VM.Models.EvaluationResult>>> CreateEvaluator() =>
        async (tx, _) =>
        {
            Dictionary<(int Tag, ulong Index), (ulong Mem, ulong Steps)> results =
                await EvaluateTransactionAsync(tx).ConfigureAwait(false);

            return [.. results.Select(kv => new Plutus.VM.Models.EvaluationResult(
                kv.Key.Tag, kv.Key.Index,
                new Plutus.VM.Models.ExUnitsResult(kv.Value.Mem, kv.Value.Steps)
            ))];
        };

    /// <summary>
    /// Retrieves transaction metadata by transaction hash.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The metadata, or null if not found.</returns>
    public async Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetTransactionMetadataAsync);
        List<BlockfrostMetadataResponse>? raw = await _http.GetJsonAsync<List<BlockfrostMetadataResponse>>(
            $"txs/{Uri.EscapeDataString(txHash)}/metadata", operation, IsNotFoundBody, cancellationToken).ConfigureAwait(false);
        return raw is null or { Count: 0 } ? null : ProviderHttp.Map(raw, MapMetadata, Provider, operation);
    }

    private static Metadata MapMetadata(List<BlockfrostMetadataResponse> raw)
    {
        Dictionary<ulong, ITransactionMetadatum> metadata = [];
        foreach (BlockfrostMetadataResponse? item in raw)
        {
            if (item?.JsonMetadata is null || !ulong.TryParse(item.Label, NumberStyles.None, CultureInfo.InvariantCulture, out ulong label) ||
                ConvertToTransactionMetadatum(item.JsonMetadata) is not { } value || !metadata.TryAdd(label, value))
            {
                throw ProviderHttp.Unusable(Provider, nameof(GetTransactionMetadataAsync), "invalid or duplicate metadata label or value");
            }
        }
        return Metadata.Create(metadata);
    }

    private static ITransactionMetadatum? ConvertToTransactionMetadatum(object value) => value switch
    {
        string str => MetadataText.Create(str),
        long lng => MetadatumIntLong.Create(lng),
        int i => MetadatumIntLong.Create(i),
        JsonElement element => ConvertJsonElementToMetadatum(element),
        Dictionary<string, object> dict => MetadatumMap.Create(
            [.. dict.Select(kv => new KeyValuePair<ITransactionMetadatum, ITransactionMetadatum>(
                ConvertToTransactionMetadatum(kv.Key) ?? MetadataText.Create(kv.Key),
                ConvertToTransactionMetadatum(kv.Value) ?? MetadataText.Create(kv.Value?.ToString() ?? "")))]
        ),
        _ => value.ToString() is string s ? MetadataText.Create(s) : null
    };

    private static ITransactionMetadatum? ConvertJsonElementToMetadatum(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => MetadataText.Create(element.GetString() ?? ""),
        JsonValueKind.Number when element.TryGetInt64(out long lng) => MetadatumIntLong.Create(lng),
        JsonValueKind.Number when element.TryGetUInt64(out _) => MetadatumIntUlong.Create(element.GetUInt64()),
        JsonValueKind.Number => MetadataText.Create(element.ToString()),
        JsonValueKind.Object => MetadatumMap.Create(
            [.. element.EnumerateObject().Select(prop => new KeyValuePair<ITransactionMetadatum, ITransactionMetadatum>(
                MetadataText.Create(prop.Name), ConvertJsonElementToMetadatum(prop.Value) ?? MetadataText.Create("")))]
        ),
        JsonValueKind.Array => MetadatumList.Create(
            [.. element.EnumerateArray()
                    .Select(ConvertJsonElementToMetadatum)
                    .Where(m => m != null)
                    .Cast<ITransactionMetadatum>()]
        ),
        JsonValueKind.Undefined => MetadataText.Create(element.ToString()),
        JsonValueKind.Null => MetadataText.Create(element.ToString()),
        JsonValueKind.True => MetadataText.Create(element.ToString()),
        JsonValueKind.False => MetadataText.Create(element.ToString()),
        _ => MetadataText.Create(element.ToString())
    };

    private string GetBaseUrl() => NetworkType switch
    {
        NetworkType.Mainnet => "https://cardano-mainnet.blockfrost.io/api/v0/",
        NetworkType.Preview => "https://cardano-preview.blockfrost.io/api/v0/",
        NetworkType.Preprod => "https://cardano-preprod.blockfrost.io/api/v0/",
        NetworkType.Testnet => "https://cardano-testnet.blockfrost.io/api/v0/",
        NetworkType.Unknown => throw new NotImplementedException(),
        _ => throw new ArgumentException($"Unsupported network type: {NetworkType}")
    };

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
        _handler?.Dispose();
    }
}
