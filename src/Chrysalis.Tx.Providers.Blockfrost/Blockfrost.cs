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
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using CborReader = SAIB.Cbor.Serialization.CborReader;
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Represents a metadata response item from the Blockfrost API.
/// </summary>
/// <param name="Label">The metadata label.</param>
/// <param name="Metadata">The hexadecimal CBOR of the single-label map.</param>
public record BlockfrostMetadataResponse(
    [property: JsonPropertyName("label")] ulong? Label,
    [property: JsonPropertyName("metadata")] string? Metadata);

/// <summary>
/// Blockfrost data provider. Ratios preserve the published decimals, not non-terminating ledger fractions.
/// Metadata retains ledger value types but not the transaction's original encoding. Paged reads are not snapshots.
/// An output is known to be spent only when its transaction response includes <c>consumed_by_tx</c>.
/// A null script hash on address lookup can hide a reference script. Spending that output without the script
/// reference on its resolved output omits its reference-script fee and the node rejects the transaction with
/// <c>FeeTooSmallUTxO</c>. Populate the resolved input's script reference (not merely a script witness), or fetch
/// the input by out-ref with <see cref="GetUtxoByOutRefAsync"/> so the reference script is known.
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
        if (parameters.MinFeeRefScriptCostPerByte is null && parameters.ProtocolMajorVer is null or >= 9)
        {
            throw ProviderHttp.Unusable(Provider, nameof(GetParametersAsync), "missing reference-script price");
        }

        CostMdls? costModels = null;
        if (parameters.CostModelsRaw is { } raw)
        {
            CborList<long>?[] models = new CborList<long>?[3];
            foreach ((string key, long[] value) in raw)
            {
                int version = key switch
                {
                    "PlutusV1" => 0,
                    "PlutusV2" => 1,
                    "PlutusV3" => 2,
                    _ => throw new InvalidOperationException("Unknown cost model key.")
                };
                models[version] = CborList.Create(value);
            }
            costModels = new CostMdls(models[0], models[1], models[2]);
        }

        CborRationalNumber[]? pool = Rationals(parameters.PvtMotionNoConfidence, parameters.PvtCommitteeNormal,
            parameters.PvtCommitteeNoConfidence, parameters.PvtHardForkInitiation, parameters.PvtPPSecurityGroup);
        CborRationalNumber[]? drep = Rationals(parameters.DvtMotionNoConfidence, parameters.DvtCommitteeNormal,
            parameters.DvtCommitteeNoConfidence, parameters.DvtUpdateToConstitution, parameters.DvtHardForkInitiation,
            parameters.DvtPPNetworkGroup, parameters.DvtPPEconomicGroup, parameters.DvtPPTechnicalGroup,
            parameters.DvtPPGovGroup, parameters.DvtTreasuryWithdrawal);
        CborRationalNumber[]? prices = Rationals(parameters.PriceMem, parameters.PriceStep);

        return new ProtocolParams(
            parameters.MinFeeA, parameters.MinFeeB, parameters.MaxBlockSize, parameters.MaxTxSize,
            parameters.MaxBlockHeaderSize, parameters.KeyDeposit, parameters.PoolDeposit, parameters.EMax, parameters.NOpt,
            Ratio(parameters.A0), Ratio(parameters.Rho), Ratio(parameters.Tau),
            parameters.ProtocolMajorVer is { } major && parameters.ProtocolMinorVer is { } minor
                ? ProtocolVersion.Create(major, minor) : null,
            parameters.MinPoolCost, parameters.CoinsPerUtxoSize, costModels,
            prices is null ? null : ExUnitPrices.Create(prices[0], prices[1]),
            parameters.MaxTxExMem is { } txMem && parameters.MaxTxExSteps is { } txSteps ? ExUnits.Create(txMem, txSteps) : null,
            parameters.MaxBlockExMem is { } blockMem && parameters.MaxBlockExSteps is { } blockSteps ? ExUnits.Create(blockMem, blockSteps) : null,
            parameters.MaxValSize, parameters.CollateralPercent, parameters.MaxCollateralInputs,
            pool is null ? null : PoolVotingThresholds.Create(pool[0], pool[1], pool[2], pool[3], pool[4]),
            drep is null ? null : DRepVotingThresholds.Create(drep[0], drep[1], drep[2], drep[3], drep[4],
                drep[5], drep[6], drep[7], drep[8], drep[9]),
            parameters.CommitteeMinSize, parameters.CommitteeMaxTermLength, parameters.GovActionLifetime,
            parameters.GovActionDeposit, parameters.DRepDeposit, parameters.DRepActivity,
            Ratio(parameters.MinFeeRefScriptCostPerByte));

        static CborRationalNumber? Ratio(decimal? value) => value is { } present ? ProviderValues.ExactRational(present) : null;
        static CborRationalNumber[]? Rationals(params decimal?[] values) => values.Any(value => value is null)
            ? null : [.. values.Select(value => ProviderValues.ExactRational(value!.Value))];
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
                if (utxo?.OutputIndex is not { } index) { throw ProviderHttp.Unusable(Provider, operation, "missing or invalid output index"); }
                found.Add(address, await ProcessUtxoAsync(utxo, index, operation, ct).ConfigureAwait(false));
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
            if (amount?.Unit is not { } unit || amount.Quantity is not { } quantity)
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
        IDatumOption? datum = utxo switch
        {
            { InlineDatum: { } inline } => InlineDatumOption.Create(1, new CborEncodedValue(HexStringCache.FromHexString(inline))),
            { DataHash: { Length: 64 } dataHash } => DatumHashOption.Create(0, HexStringCache.FromHexString(dataHash)),
            { DataHash: not null } => throw ProviderHttp.Unusable(Provider, operation, "invalid datum hash"),
            _ => null
        };
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
        if (language is not ("timelock" or "plutusV1" or "plutusV2" or "plutusV3"))
        {
            throw ProviderHttp.Unusable(Provider, operation, "unknown script language");
        }
        string endpoint = language == "timelock" ? "json" : "cbor";
        using JsonDocument body = (await _http.GetJsonAsync<JsonDocument>($"{path}/{endpoint}", operation, null, cancellationToken).ConfigureAwait(false))!;
        IScript script = ProviderHttp.Map<JsonElement, IScript>(body.RootElement, element =>
        {
            if (language == "timelock")
            {
                JsonElement native = element.GetProperty("json");
                if (native.ValueKind != JsonValueKind.Object) { throw ProviderHttp.Unusable(Provider, operation, "missing native script object"); }
                return MultiSigScript.Create(0, ProviderValues.NativeScript(native));
            }
            byte[] bytes = HexStringCache.FromHexString(element.GetProperty("cbor").GetString()
                ?? throw ProviderHttp.Unusable(Provider, operation, "missing script bytes"));
            return language switch
            {
                "plutusV1" => PlutusV1Script.Create(1, bytes),
                "plutusV2" => PlutusV2Script.Create(2, bytes),
                _ => PlutusV3Script.Create(3, bytes)
            };
        }, Provider, operation);
        if (!string.Equals(script.HashHex(), scriptHash, StringComparison.OrdinalIgnoreCase))
        {
            throw ProviderHttp.Unusable(Provider, operation, "script hash differs from the requested hash");
        }
        return script;
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
        BlockfrostUtxo? utxo = await ReadTxOutputAsync(txHash, index, operation, cancellationToken).ConfigureAwait(false);
        if (utxo is null) { return null; }
        if (utxo.ConsumedByTx is { } consumed)
        {
            if (consumed.Length != 64 || !consumed.All(Uri.IsHexDigit))
            {
                throw ProviderHttp.Unusable(Provider, operation, "invalid consumed-by transaction hash");
            }
            return null;
        }
        return await ProcessUtxoAsync(utxo, index, operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BlockfrostUtxo?> ReadTxOutputAsync(string txHash, ulong index, string operation, CancellationToken cancellationToken)
    {
        using JsonDocument? body = await _http.GetJsonAsync<JsonDocument>($"txs/{Uri.EscapeDataString(txHash)}/utxos",
            operation, IsNotFoundBody, cancellationToken).ConfigureAwait(false);
        if (body is null) { return null; }
        return ProviderHttp.Map(body.RootElement, root =>
        {
            BlockfrostUtxo? match = null;
            foreach (JsonElement item in root.GetProperty("outputs").EnumerateArray())
            {
                if (item.TryGetProperty("collateral", out JsonElement collateral) && collateral.ValueKind == JsonValueKind.True) { continue; }
                if (item.GetProperty("output_index").Deserialize<ulong>(JsonSerializerOptions.Web) != index) { continue; }
                if (match is not null) { throw ProviderHttp.Unusable(Provider, operation, "repeated output"); }
                match = item.Deserialize<BlockfrostUtxo>(JsonSerializerOptions.Web) ?? throw ProviderHttp.Unusable(Provider, operation, "null output");
                match.TxHash = txHash;
            }
            return match;
        }, Provider, operation);
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
            $"txs/{Uri.EscapeDataString(txHash)}/metadata/cbor", operation, IsNotFoundBody, cancellationToken).ConfigureAwait(false);
        return raw is null or { Count: 0 } ? null : ProviderHttp.Map(raw, MapMetadata, Provider, operation);
    }

    private static Metadata MapMetadata(List<BlockfrostMetadataResponse> raw)
    {
        Dictionary<ulong, ITransactionMetadatum> metadata = [];
        foreach (BlockfrostMetadataResponse? item in raw)
        {
            if (item is not { Label: { } label, Metadata: { } hex })
            {
                throw ProviderHttp.Unusable(Provider, nameof(GetTransactionMetadataAsync), "missing metadata label or value");
            }
            byte[] bytes = Convert.FromHexString(hex);
            Metadata map = CborSerializer.Deserialize<Metadata>(bytes, out int consumed);
            if (consumed != bytes.Length || !HasSingleMetadataEntry(bytes) || map.Value is not { Count: 1 } entries ||
                !entries.TryGetValue(label, out ITransactionMetadatum? value) || !metadata.TryAdd(label, value))
            {
                throw ProviderHttp.Unusable(Provider, nameof(GetTransactionMetadataAsync), "invalid or duplicate metadata label or value");
            }
        }
        return Metadata.Create(metadata);
    }

    // The codec produces a dictionary, so its count alone cannot detect a repeated wire key.
    private static bool HasSingleMetadataEntry(ReadOnlySpan<byte> bytes)
    {
        CborReader reader = new(bytes);
        reader.ReadBeginMap();
        int size = reader.ReadSize();
        if (size >= 0) { return size == 1; }
        reader.SkipDataItem();
        reader.SkipDataItem();
        return reader.Buffer is [0xff];
    }

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
