using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using CborReader = SAIB.Cbor.Serialization.CborReader;
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Tx.Providers;

/// <summary>
/// Koios API v1 data provider. Protocol parameters are the node's answer as Koios stored it once per epoch.
/// Output lookups by reference return only unspent outputs. Addresses must be Bech32. Native reference scripts
/// are mapped from db-sync JSON, so a script not canonically encoded on chain fails its hash check. Results carry
/// no chain point. Each address read costs one request per page plus one empty terminating page, and a multi-page
/// read can miss an output spent between pages.
/// </summary>
public sealed class Koios : ICardanoDataProvider, IDisposable
{
    private const string Provider = nameof(Koios);
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler? _handler;
    private readonly ProviderHttp _http;

    /// <summary>Gets the Cardano network type for this provider.</summary>
    public NetworkType NetworkType { get; }

    /// <summary>Initializes a new Koios provider.</summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="token">Optional Bearer JWT token.</param>
    /// <param name="url">Optional custom Koios API base URL.</param>
    /// <param name="handler">Optional caller-owned HTTP handler.</param>
    public Koios(NetworkType networkType = NetworkType.Preview, string? token = null, string url = "", HttpMessageHandler? handler = null)
    {
        Uri configuredUri = BaseUri(networkType, url);
        if (!string.IsNullOrEmpty(token) && token.Any(ch => ch is < '!' or > '~'))
        {
            throw new ArgumentException("The Koios bearer token must contain only printable ASCII characters.", nameof(token));
        }
        Uri baseAddress = new UriBuilder(configuredUri) { Path = configuredUri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
        NetworkType = networkType;
        _handler = handler is null ? new HttpClientHandler { CheckCertificateRevocationList = true } : null;
        _httpClient = new HttpClient(handler ?? _handler!, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        List<string> secrets = [.. ProviderHttp.SecretsOf(configuredUri)];
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            secrets.Add(token);
        }
        _http = new(_httpClient, Provider, secrets);
    }

    private static Uri BaseUri(NetworkType networkType, string url) => !string.IsNullOrEmpty(url)
        ? new Uri(url)
        : new Uri(networkType switch
        {
            NetworkType.Mainnet => "https://api.koios.rest/api/v1/",
            NetworkType.Preview => "https://preview.koios.rest/api/v1/",
            NetworkType.Preprod => "https://preprod.koios.rest/api/v1/",
            NetworkType.Testnet or NetworkType.Unknown or _ => throw new ArgumentException($"Unsupported network type: {networkType}", nameof(networkType))
        });

    /// <summary>Retrieves unspent outputs for the specified Bech32 addresses.</summary>
    /// <param name="address">The list of Bech32 addresses to query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The resolved inputs in request and backend order.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        foreach (string item in address) { ArgumentException.ThrowIfNullOrWhiteSpace(item, nameof(address)); }
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetUtxosAsync);
        const int pageSize = 1000;
        ResolvedInputSet found = new(Provider, operation);
        foreach (string item in address.Distinct(StringComparer.Ordinal))
        {
            long offset = 0;
            await ProviderHttp.ReadAllPagesAsync(ReadPage, 0, pageSize, shortPageEnds: false, AddPage, Provider, operation, cancellationToken).ConfigureAwait(false);

            async Task<List<ResolvedInput>> ReadPage(int _, CancellationToken ct)
            {
                string path = string.Create(CultureInfo.InvariantCulture, $"address_utxos?offset={offset}&limit={pageSize}&order=tx_hash.asc,tx_index.asc");
                JsonObject body = new() { ["_addresses"] = new JsonArray(item), ["_extended"] = true };
                using JsonDocument document = await PostJsonAsync(path, body, operation, ct).ConfigureAwait(false);
                return ProviderHttp.Map<JsonElement, List<ResolvedInput>>(document.RootElement, root => [.. root.EnumerateArray().Select(row => MapOutput(row, operation))], Provider, operation);
            }

            Task AddPage(List<ResolvedInput> page, CancellationToken _)
            {
                foreach (ResolvedInput input in page) { found.Add(item, input); }
                offset += page.Count;
                return Task.CompletedTask;
            }
        }
        return found.ToList();
    }

    private async Task<JsonDocument> PostJsonAsync(string path, JsonObject body, string operation, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = (await _http.SendAsync(request, operation, null, cancellationToken).ConfigureAwait(false))!;
        return await _http.ReadJsonAsync<JsonDocument>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    private static ResolvedInput MapOutput(JsonElement row, string operation)
    {
        if (row.GetProperty("tx_hash").GetString() is not { Length: 64 } txHash
            || row.GetProperty("address").GetString() is not { Length: > 0 } address)
        {
            throw ProviderHttp.Unusable(Provider, operation, "missing or invalid output fields");
        }
        ulong index = row.GetProperty("tx_index").GetUInt64();
        ulong lovelace = Amount(row.GetProperty("value"));
        List<(ReadOnlyMemory<byte> Policy, ReadOnlyMemory<byte> Name, ulong Quantity)> assets = [];
        foreach (JsonElement asset in row.GetProperty("asset_list").EnumerateArray())
        {
            string policy = asset.GetProperty("policy_id").GetString() ?? throw ProviderHttp.Unusable(Provider, operation, "missing asset policy");
            string name = asset.GetProperty("asset_name").GetString() ?? throw ProviderHttp.Unusable(Provider, operation, "missing asset name");
            assets.Add((HexStringCache.FromHexString(policy), HexStringCache.FromHexString(name), Amount(asset.GetProperty("quantity"))));
        }
        IValue value = ProviderValues.Create(lovelace, assets, Provider, operation);
        IDatumOption? datum = null;
        if (row.TryGetProperty("inline_datum", out JsonElement inlineDatum) && inlineDatum.ValueKind != JsonValueKind.Null)
        {
            string? inlineBytes = inlineDatum.GetProperty("bytes").GetString();
            if (string.IsNullOrEmpty(inlineBytes)) { throw ProviderHttp.Unusable(Provider, operation, "missing inline datum bytes"); }
            datum = InlineDatumOption.Create(1, new CborEncodedValue(HexStringCache.FromHexString(inlineBytes)));
        }
        else if (row.TryGetProperty("datum_hash", out JsonElement datumHashElement) && datumHashElement.ValueKind != JsonValueKind.Null)
        {
            string? datumHash = datumHashElement.GetString();
            if (datumHash is not { Length: 64 }) { throw ProviderHttp.Unusable(Provider, operation, "invalid datum hash"); }
            datum = DatumHashOption.Create(0, HexStringCache.FromHexString(datumHash));
        }
        CborEncodedValue? scriptRef = row.TryGetProperty("reference_script", out JsonElement referenceScript)
            && referenceScript.ValueKind != JsonValueKind.Null
            ? new CborEncodedValue(CborSerializer.Serialize(Script(referenceScript, operation))) : null;
        TransactionInput outref = TransactionInput.Create(HexStringCache.FromHexString(txHash), index);
        ITransactionOutput output = PostAlonzoTransactionOutput.Create(
            new Address(ChrysalisWallet.Address.FromBech32(address).ToBytes()), value, datum, scriptRef);
        return new(outref, output);
    }

    private static IScript Script(JsonElement reference, string operation)
    {
        if (reference.GetProperty("hash").GetString() is not { Length: 56 } hash || !hash.All(Uri.IsHexDigit))
        {
            throw ProviderHttp.Unusable(Provider, operation, "invalid script hash");
        }
        string? type = reference.GetProperty("type").GetString();
        if (type == "timelock")
        {
            JsonElement value = reference.GetProperty("value");
            if (value.ValueKind != JsonValueKind.Object) { throw ProviderHttp.Unusable(Provider, operation, "missing native script object"); }
            return ProviderValues.RequireScriptHash(MultiSigScript.Create(0, ProviderValues.NativeScript(value)), hash, Provider, operation);
        }
        if (type is not ("plutusV1" or "plutusV2" or "plutusV3"))
        {
            throw ProviderHttp.Unusable(Provider, operation, "unknown script type");
        }
        string? bytesHex = reference.GetProperty("bytes").GetString();
        if (string.IsNullOrEmpty(bytesHex)) { throw ProviderHttp.Unusable(Provider, operation, "missing script bytes"); }
        byte[] bytes = HexStringCache.FromHexString(bytesHex);
        IScript script = CreatePlutus(type, bytes);
        if (!string.Equals(script.HashHex(), hash, StringComparison.OrdinalIgnoreCase))
        {
            CborReader reader = new(bytes);
            byte[] unwrapped = reader.ReadByteStringToArray();
            if (!reader.Buffer.IsEmpty) { throw ProviderHttp.Unusable(Provider, operation, "trailing script bytes"); }
            script = CreatePlutus(type, unwrapped);
        }
        return ProviderValues.RequireScriptHash(script, hash, Provider, operation);

        static IScript CreatePlutus(string kind, byte[] scriptBytes) => kind switch
        {
            "plutusV1" => PlutusV1Script.Create(1, scriptBytes),
            "plutusV2" => PlutusV2Script.Create(2, scriptBytes),
            _ => PlutusV3Script.Create(3, scriptBytes)
        };
    }

    private static ulong Amount(JsonElement element) => element.Deserialize<ulong>(JsonSerializerOptions.Web);

    /// <summary>Retrieves the epoch's stored protocol parameters from Koios.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The protocol parameters.</returns>
    public async Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetParametersAsync);
        using JsonDocument document = (await _http.GetJsonAsync<JsonDocument>("cli_protocol_params", operation, null, cancellationToken).ConfigureAwait(false))!;
        return ProviderHttp.Map(document.RootElement, MapParameters, Provider, operation);
    }

    private static ProtocolParams MapParameters(JsonElement root)
    {
        CborList<long>?[] models = new CborList<long>?[3];
        foreach (JsonProperty entry in root.GetProperty("costModels").EnumerateObject())
        {
            int version = entry.Name switch
            {
                "PlutusV1" => 0,
                "PlutusV2" => 1,
                "PlutusV3" => 2,
                _ => throw new InvalidOperationException("Unknown cost model key.")
            };
            models[version] = CborList.Create(entry.Value.EnumerateArray().Select(item => item.GetInt64()));
        }
        JsonElement versionElement = root.GetProperty("protocolVersion");
        JsonElement prices = root.GetProperty("executionUnitPrices");
        JsonElement txUnits = root.GetProperty("maxTxExecutionUnits");
        JsonElement blockUnits = root.GetProperty("maxBlockExecutionUnits");
        JsonElement pool = root.GetProperty("poolVotingThresholds");
        JsonElement drep = root.GetProperty("dRepVotingThresholds");
        return new ProtocolParams(
            U(root, "txFeePerByte"), U(root, "txFeeFixed"), U(root, "maxBlockBodySize"), U(root, "maxTxSize"),
            U(root, "maxBlockHeaderSize"), U(root, "stakeAddressDeposit"), U(root, "stakePoolDeposit"),
            U(root, "poolRetireMaxEpoch"), U(root, "stakePoolTargetNum"),
            Ratio(root.GetProperty("poolPledgeInfluence")), Ratio(root.GetProperty("monetaryExpansion")),
            Ratio(root.GetProperty("treasuryCut")),
            ProtocolVersion.Create(U(versionElement, "major"), U(versionElement, "minor")),
            U(root, "minPoolCost"), U(root, "utxoCostPerByte"),
            new CostMdls(models[0], models[1], models[2]),
            ExUnitPrices.Create(Ratio(prices.GetProperty("priceMemory")), Ratio(prices.GetProperty("priceSteps"))),
            ExUnits.Create(U(txUnits, "memory"), U(txUnits, "steps")),
            ExUnits.Create(U(blockUnits, "memory"), U(blockUnits, "steps")),
            U(root, "maxValueSize"), U(root, "collateralPercentage"), U(root, "maxCollateralInputs"),
            PoolVotingThresholds.Create(
                Ratio(pool.GetProperty("motionNoConfidence")), Ratio(pool.GetProperty("committeeNormal")),
                Ratio(pool.GetProperty("committeeNoConfidence")), Ratio(pool.GetProperty("hardForkInitiation")),
                Ratio(pool.GetProperty("ppSecurityGroup"))),
            DRepVotingThresholds.Create(
                Ratio(drep.GetProperty("motionNoConfidence")), Ratio(drep.GetProperty("committeeNormal")),
                Ratio(drep.GetProperty("committeeNoConfidence")), Ratio(drep.GetProperty("updateToConstitution")),
                Ratio(drep.GetProperty("hardForkInitiation")), Ratio(drep.GetProperty("ppNetworkGroup")),
                Ratio(drep.GetProperty("ppEconomicGroup")), Ratio(drep.GetProperty("ppTechnicalGroup")),
                Ratio(drep.GetProperty("ppGovGroup")), Ratio(drep.GetProperty("treasuryWithdrawal"))),
            U(root, "committeeMinSize"), U(root, "committeeMaxTermLength"), U(root, "govActionLifetime"),
            U(root, "govActionDeposit"), U(root, "dRepDeposit"), U(root, "dRepActivity"),
            Ratio(root.GetProperty("minFeeRefScriptCostPerByte")));

        static ulong U(JsonElement element, string name) => element.GetProperty(name).GetUInt64();

        static CborRationalNumber Ratio(JsonElement element) => element.ValueKind == JsonValueKind.Number
            ? ProviderValues.ExactRational(element.GetDecimal())
            : element.ValueKind == JsonValueKind.Object
                ? ObjectRatio(element)
                : throw new InvalidOperationException("Invalid rational value.");

        static CborRationalNumber ObjectRatio(JsonElement element)
        {
            ulong numerator = U(element, "numerator");
            ulong denominator = U(element, "denominator");
            return denominator == 0
                ? throw new ArgumentOutOfRangeException(nameof(element), "Zero denominator.")
                : CborRationalNumber.Create(numerator, denominator);
        }
    }

    /// <summary>Retrieves a single unspent output by its transaction hash and index.</summary>
    /// <param name="txHash">The transaction hash in hex.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The unspent output, or null if absent or already spent.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetUtxoByOutRefAsync);
        string utxoRef = string.Create(CultureInfo.InvariantCulture, $"{txHash}#{index}");
        JsonObject body = new() { ["_utxo_refs"] = new JsonArray(utxoRef), ["_extended"] = true };
        using JsonDocument document = await PostJsonAsync("utxo_info", body, operation, cancellationToken).ConfigureAwait(false);
        return ProviderHttp.Map(document.RootElement, root =>
        {
            int rows = root.GetArrayLength();
            if (rows == 0) { return null; }
            JsonElement item = rows == 1 ? root[0] : throw ProviderHttp.Unusable(Provider, operation, "multiple output matches");
            if (!string.Equals(item.GetProperty("tx_hash").GetString(), txHash, StringComparison.OrdinalIgnoreCase)
                || item.GetProperty("tx_index").GetUInt64() != index)
            {
                throw ProviderHttp.Unusable(Provider, operation, "wrong output reference");
            }
            return item.GetProperty("is_spent").GetBoolean() ? null : MapOutput(item, operation);
        }, Provider, operation);
    }

    /// <summary>Retrieves transaction metadata by transaction hash from the transaction CBOR.</summary>
    /// <param name="txHash">The transaction hash in hex.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The metadata, or null if the transaction is absent or carries no metadata.</returns>
    public async Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(GetTransactionMetadataAsync);
        JsonObject body = new() { ["_tx_hashes"] = new JsonArray(txHash) };
        using JsonDocument document = await PostJsonAsync("tx_cbor", body, operation, cancellationToken).ConfigureAwait(false);
        return ProviderHttp.Map(document.RootElement, root =>
        {
            int rows = root.GetArrayLength();
            if (rows == 0) { return null; }
            JsonElement item = rows == 1 ? root[0] : throw ProviderHttp.Unusable(Provider, operation, "multiple transaction matches");
            if (!string.Equals(item.GetProperty("tx_hash").GetString(), txHash, StringComparison.OrdinalIgnoreCase))
            {
                throw ProviderHttp.Unusable(Provider, operation, "wrong transaction hash");
            }
            string cbor = item.GetProperty("cbor").GetString() ?? throw ProviderHttp.Unusable(Provider, operation, "missing transaction cbor");
            return ProviderValues.TransactionMetadata(Convert.FromHexString(cbor));
        }, Provider, operation);
    }

    /// <summary>Submits a signed transaction to Koios.</summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction hash.</returns>
    public async Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        cancellationToken.ThrowIfCancellationRequested();
        const string operation = nameof(SubmitTransactionAsync);
        using HttpRequestMessage request = new(HttpMethod.Post, "submittx")
        {
            Content = new ByteArrayContent(CborSerializer.Serialize(tx)) { Headers = { ContentType = new MediaTypeHeaderValue("application/cbor") } }
        };
        return await _http.SubmitAsync(request, operation,
            (response, ct) => _http.ReadJsonAsync<string>(response, operation, ct), jsonRpc: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Disposes the underlying HTTP client and owned handler.</summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        _handler?.Dispose();
    }
}
