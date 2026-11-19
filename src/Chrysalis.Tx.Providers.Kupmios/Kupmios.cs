using System.Globalization;
using System.Text;
using System.Text.Json;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Tx.Models;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Kupo and Ogmios data provider. Protocol parameters are built in for the network.
/// </summary>
public sealed class Kupmios : ICardanoDataProvider, IDisposable
{
    private static readonly JsonSerializerOptions KupoJson = new(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false };
    private readonly HttpClient _httpClient;
    private readonly HttpClient _ogmiosClient;
    private readonly ProviderHttp _kupo;
    private readonly ProviderHttp _ogmios;
    private const string Provider = nameof(Kupmios);

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
    /// <param name="handler">Optional caller-owned HTTP handler.</param>
    public Kupmios(string kupoEndpoint, string ogmiosEndpoint, NetworkType networkType = NetworkType.Preview, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(kupoEndpoint);
        ArgumentNullException.ThrowIfNull(ogmiosEndpoint);

        Uri kupoUri = new(kupoEndpoint);
        Uri ogmiosUri = new(ogmiosEndpoint);
        _httpClient = CreateHttpClient(kupoUri, handler);
        _ogmiosClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ogmiosClient.BaseAddress = ogmiosUri;
        _ogmiosClient.Timeout = TimeSpan.FromSeconds(30);
        NetworkType = networkType;
        string[] secrets = [.. ProviderHttp.SecretsOf(kupoUri), .. ProviderHttp.SecretsOf(ogmiosUri)];
        _kupo = new(_httpClient, Provider, secrets, KupoJson);
        _ogmios = new(_ogmiosClient, Provider, secrets);
    }

    /// <summary>
    /// Retrieves UTxOs for multiple addresses via Kupo.
    /// </summary>
    /// <param name="address">The list of addresses or patterns.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        foreach (string item in address) { ArgumentException.ThrowIfNullOrWhiteSpace(item, nameof(address)); }
        cancellationToken.ThrowIfCancellationRequested();
        string[] patterns = [.. address.Distinct(StringComparer.Ordinal)];
        const string operation = nameof(GetUtxosAsync);
        List<KupoMatch>[] answers = await ProviderHttp.AwaitBatchAsync(
            Task.WhenAll(patterns.Select(pattern => FetchMatchesAsync(pattern, operation, cancellationToken))),
            cancellationToken).ConfigureAwait(false);
        ResolvedInputSet merged = new(Provider, operation);
        for (int i = 0; i < patterns.Length; i++)
        {
            foreach (KupoMatch match in answers[i])
            {
                cancellationToken.ThrowIfCancellationRequested();
                merged.Add(patterns[i], ProviderHttp.Map(match, item => MapMatch(item, operation), Provider, operation));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return merged.ToList();
    }

    /// <summary>
    /// Retrieves UTxOs by payment key hash pattern.
    /// </summary>
    /// <param name="paymentKey">The payment key hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosByPaymentKeyAsync(string paymentKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentKey);
        cancellationToken.ThrowIfCancellationRequested();

        string pattern = paymentKey.EndsWith("/*", StringComparison.Ordinal)
            ? paymentKey
            : $"{paymentKey}/*";

        return GetUtxosAsync([pattern], cancellationToken);
    }

    /// <summary>
    /// Retrieves UTxOs by multiple payment key hash patterns.
    /// </summary>
    /// <param name="paymentKeys">The payment key hashes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosByPaymentKeysAsync(List<string> paymentKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentKeys);
        foreach (string key in paymentKeys) { ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(paymentKeys)); }
        cancellationToken.ThrowIfCancellationRequested();

        List<string> patterns = [.. paymentKeys.Select(key => key.EndsWith("/*", StringComparison.Ordinal) ? key : $"{key}/*")];

        return GetUtxosAsync(patterns, cancellationToken);
    }

    /// <summary>
    /// Retrieves a UTxO by its output reference (transaction hash + index).
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The resolved input, or null if not found.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        ParseHash(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetUtxoByOutRefAsync);
        string pattern = string.Create(CultureInfo.InvariantCulture, $"{index}@{txHash}");
        List<KupoMatch> matches = await FetchMatchesAsync(pattern, operation, cancellationToken).ConfigureAwait(false);
        if (matches.Count == 0) { return null; }
        if (matches.Count != 1) { throw ProviderHttp.Unusable(Provider, operation, "multiple output matches"); }
        ResolvedInput input = ProviderHttp.Map(matches[0], match => MapMatch(match, operation), Provider, operation);
        if (input.Outref.Index != index || !string.Equals(Convert.ToHexString(input.Outref.TransactionId.Span), txHash, StringComparison.OrdinalIgnoreCase))
        {
            throw ProviderHttp.Unusable(Provider, operation, "wrong output reference");
        }
        return input;
    }

    private static void ParseHash(string txHash)
    {
        ArgumentNullException.ThrowIfNull(txHash);
        if (txHash.Length != 64 || Convert.FromHexString(txHash, stackalloc byte[32], out _, out _) != System.Buffers.OperationStatus.Done)
        {
            throw new ArgumentException("Expected a 32-byte transaction hash in hex.", nameof(txHash));
        }
    }

    /// <summary>
    /// Retrieves a UTxO by its TransactionInput reference.
    /// </summary>
    /// <param name="outRef">The transaction input reference.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The resolved input, or null if not found.</returns>
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(TransactionInput outRef, CancellationToken cancellationToken = default) => GetUtxoByOutRefAsync(Convert.ToHexString(outRef.TransactionId.Span), outRef.Index, cancellationToken);

    /// <summary>
    /// Retrieves UTxOs by multiple output references.
    /// </summary>
    /// <param name="outRefs">The output references as tuples.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public async Task<List<ResolvedInput>> GetUtxosByOutRefsAsync(List<(string TxHash, ulong OutputIndex)> outRefs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outRefs);
        foreach ((string hash, _) in outRefs) { ParseHash(hash); }
        cancellationToken.ThrowIfCancellationRequested();
        (string TxHash, ulong OutputIndex)[] requests = [.. outRefs.DistinctBy(r => (r.TxHash.ToUpperInvariant(), r.OutputIndex))];
        ResolvedInput?[] results = await ProviderHttp.AwaitBatchAsync(
            Task.WhenAll(requests.Select(r => GetUtxoByOutRefAsync(r.TxHash, r.OutputIndex, cancellationToken))),
            cancellationToken).ConfigureAwait(false);
        return [.. results.OfType<ResolvedInput>()];
    }

    /// <summary>
    /// Retrieves UTxOs by multiple TransactionInput references.
    /// </summary>
    /// <param name="outRefs">The transaction input references.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosByOutRefsAsync(List<TransactionInput> outRefs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outRefs);
        return GetUtxosByOutRefsAsync([.. outRefs.Select(item => (Convert.ToHexString(item.TransactionId.Span), item.Index))], cancellationToken);
    }

    /// <summary>
    /// Retrieves protocol parameters (uses defaults for each network).
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The protocol parameters.</returns>
    public Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProtocolParametersResponse parameters = NetworkType switch
        {
            NetworkType.Mainnet => PParamsDefaults.Mainnet(),
            NetworkType.Preview => PParamsDefaults.Preview(),
            NetworkType.Preprod => PParamsDefaults.Preview(),
            NetworkType.Testnet => PParamsDefaults.Preview(),
            NetworkType.Unknown => throw new NotSupportedException("No built-in parameters exist for this network."),
            _ => throw new ArgumentException($"Unsupported network type: {NetworkType}")
        };

        return Task.FromResult(ProviderHttp.Map(parameters, MapParameters, Provider, nameof(GetParametersAsync)));
    }

    private static ProtocolParams MapParameters(ProtocolParametersResponse parameters)
    {
        Dictionary<int, ICborMaybeIndefList<long>> costMdls = [];

        foreach ((string key, int[] value) in parameters.CostModelsRaw ?? [])
        {
            int version = key switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new InvalidOperationException("Unknown cost model key.")
            };

            costMdls[version] = CborDefList<long>.Create([.. value.Select(x => (long)x)]);
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
            CborRationalNumber.Create((ulong)(parameters.A0 * 100), 100),
            CborRationalNumber.Create((ulong)(parameters.Rho * 100), 100),
            CborRationalNumber.Create((ulong)(parameters.Tau * 100), 100),
            ProtocolVersion.Create(9, 0),
            ulong.Parse(parameters.MinPoolCost, CultureInfo.InvariantCulture),
            ulong.Parse(parameters.CoinsPerUtxoSize, CultureInfo.InvariantCulture),
            new CostMdls(costMdls.GetValueOrDefault(0), costMdls.GetValueOrDefault(1), costMdls.GetValueOrDefault(2)),
            ExUnitPrices.Create(
                CborRationalNumber.Create((ulong)(parameters.PriceMem * 1000000), 1000000),
                CborRationalNumber.Create((ulong)(parameters.PriceStep * 1000000), 1000000)
            ),
            ExUnits.Create(ulong.Parse(parameters.MaxTxExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxTxExSteps, CultureInfo.InvariantCulture)),
            ExUnits.Create(ulong.Parse(parameters.MaxBlockExMem, CultureInfo.InvariantCulture), ulong.Parse(parameters.MaxBlockExSteps, CultureInfo.InvariantCulture)),
            ulong.Parse(parameters.MaxValSize, CultureInfo.InvariantCulture),
            (ulong)parameters.CollateralPercent,
            (ulong)parameters.MaxCollateralInputs,
            PoolVotingThresholds.Create(
                CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1),
                CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1)
            ),
            DRepVotingThresholds.Create(
                CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1),
                CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1),
                CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1),
                CborRationalNumber.Create(1, 1)
            ),
            1,
            1,
            1,
            1,
            1,
            1,
            CborRationalNumber.Create((ulong)parameters.MinFeeRefScriptCostPerByte, 1)
        );

        return protocolParams;
    }

    /// <summary>
    /// Submits a signed transaction via Ogmios.
    /// </summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(SubmitTransactionAsync);
        var payload = new
        {
            jsonrpc = "2.0",
            method = "submitTransaction",
            @params = new { transaction = new { cbor = Convert.ToHexString(CborSerializer.Serialize(tx)) } },
            id = Guid.NewGuid().ToString()
        };
        using HttpRequestMessage request = new(HttpMethod.Post, (Uri?)null)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        bool receivedSuccess = false;
        (string Hash, ProviderException? Rejection) outcome;
        try
        {
            using HttpResponseMessage response = await _ogmios.SendJsonRpcAsync(request, operation, cancellationToken).ConfigureAwait(false);
            receivedSuccess = true;
            using JsonDocument body = await _ogmios.ReadJsonAsync<JsonDocument>(response, operation, cancellationToken).ConfigureAwait(false);
            outcome = ProviderHttp.Map(body.RootElement, MapSubmission, Provider, operation);
        }
        catch (ProviderException error) when (error is not ProviderStatusException && (receivedSuccess || error.IsTransient))
        {
            throw new ProviderException($"{error.Message}; the transaction may have been accepted", error.IsTransient, error.Detail);
        }
        if (outcome.Rejection is { } rejection) { throw rejection; }
        return outcome.Hash;
    }

    private (string Hash, ProviderException? Rejection) MapSubmission(JsonElement root)
    {
        const string operation = nameof(SubmitTransactionAsync);
        if (root.TryGetProperty("error", out JsonElement error))
        {
            long code = error.GetProperty("code").GetInt64();
            string? detail = error.TryGetProperty("message", out JsonElement message) ? message.GetString() : null;
            return ("", new ProviderException(string.Create(CultureInfo.InvariantCulture, $"{Provider} {operation}: JSON-RPC error {code}"),
                isTransient: false, _ogmios.RedactDetail(detail)));
        }
        string hash = root.GetProperty("result").GetProperty("transaction").GetProperty("id").GetString()
            ?? throw ProviderHttp.Unusable(Provider, operation, "missing transaction id");
        return (hash, null);
    }

    /// <summary>Transaction metadata lookup by hash is not supported by Kupo.</summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Always throws.</returns>
    public Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Kupo does not support metadata lookup by transaction hash.");
    }

    private static HttpClient CreateHttpClient(Uri endpoint, HttpMessageHandler? handler)
    {
        UriBuilder address = new(endpoint) { Path = endpoint.AbsolutePath.TrimEnd('/') + "/" };
        HttpClient client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.BaseAddress = address.Uri;
        client.Timeout = TimeSpan.FromSeconds(120);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<List<KupoMatch>> FetchMatchesAsync(string pattern, string operation, CancellationToken cancellationToken)
    {
        // Slash separates the two Kupo pattern parts; escapes protect query delimiters.
        string path = string.Join('/', pattern.Split('/').Select(Uri.EscapeDataString)).Replace("%40", "@", StringComparison.Ordinal)
            .Replace("%2A", "*", StringComparison.Ordinal);
        return (await _kupo.GetJsonAsync<List<KupoMatch>>($"matches/{path}?unspent&resolve_hashes", operation, null, cancellationToken).ConfigureAwait(false))!;
    }

    private static ResolvedInput MapMatch(KupoMatch? match, string operation)
    {
        if (match is not { TransactionId.Length: 64, OutputIndex: >= 0, Value.Coins: >= 0 } || string.IsNullOrEmpty(match.Address))
        {
            throw ProviderHttp.Unusable(Provider, operation, "missing or invalid output fields");
        }
        IEnumerable<(ReadOnlyMemory<byte> Policy, ReadOnlyMemory<byte> Name, ulong Quantity)> assets = (match.Value.Assets ?? []).Select(pair =>
        {
            string[] parts = pair.Key.Split('.', 2);
            byte[] policy = HexStringCache.FromHexString(parts[0]);
            byte[] name = parts.Length == 2 ? HexStringCache.FromHexString(parts[1]) : [];
            return ((ReadOnlyMemory<byte>)policy, (ReadOnlyMemory<byte>)name, checked((ulong)pair.Value));
        });
        TransactionInput outref = TransactionInput.Create(HexStringCache.FromHexString(match.TransactionId), (ulong)match.OutputIndex);
        IValue value = ProviderValues.Create((ulong)match.Value.Coins, assets, Provider, operation);
        IDatumOption? datum = CreateDatumOption(match, operation);
        CborEncodedValue? script = CreateScriptReference(match.IScript, operation);
        return new(outref, PostAlonzoTransactionOutput.Create(CreateAddress(match.Address), value, datum, script));
    }

    private static IDatumOption? CreateDatumOption(KupoMatch match, string operation)
    {
        if (match.DatumType == "hash" && string.IsNullOrEmpty(match.DatumHash))
        {
            throw ProviderHttp.Unusable(Provider, operation, "missing datum hash");
        }
        if (match.DatumType == "inline")
        {
            if (string.IsNullOrEmpty(match.Datum)) { throw ProviderHttp.Unusable(Provider, operation, "missing inline datum"); }
            return InlineDatumOption.Create(1, new CborEncodedValue(HexStringCache.FromHexString(match.Datum)));
        }
        return string.IsNullOrEmpty(match.DatumHash) ? null : DatumHashOption.Create(0, HexStringCache.FromHexString(match.DatumHash));
    }

    private static CborEncodedValue? CreateScriptReference(KupoScript? script, string operation)
    {
        if (script is null) { return null; }
        if (string.IsNullOrEmpty(script.IScript) || string.IsNullOrEmpty(script.Language))
        {
            throw ProviderHttp.Unusable(Provider, operation, "missing script language or bytes");
        }
        byte[] bytes = HexStringCache.FromHexString(script.IScript);
        IScript value = script.Language switch
        {
            "native" => MultiSigScript.Create(0, CborSerializer.Deserialize<INativeScript>(bytes)),
            "plutus:v1" => PlutusV1Script.Create(1, bytes),
            "plutus:v2" => PlutusV2Script.Create(2, bytes),
            "plutus:v3" => PlutusV3Script.Create(3, bytes),
            _ => throw ProviderHttp.Unusable(Provider, operation, "unknown script language")
        };
        return new(CborSerializer.Serialize(value));
    }

    private static Address CreateAddress(string bech32Address) => new(Wallet.Models.Addresses.Address.FromBech32(bech32Address).ToBytes());

    /// <summary>
    /// Disposes the underlying HTTP clients.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        _ogmiosClient.Dispose();
    }
}
