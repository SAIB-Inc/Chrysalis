using System.Text;
using System.Text.Json;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Providers;

public class Ogmios(string endpoint, NetworkType networkType = NetworkType.Preview) : ICardanoDataProvider
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(30) };

    public NetworkType NetworkType => networkType;

    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        byte[] txBytes = CborSerializer.Serialize(tx);
        string txCbor = Convert.ToHexString(txBytes).ToLowerInvariant();

        var request = new
        {
            jsonrpc = "2.0",
            method = "submitTransaction",
            @params = new { transaction = new { cbor = txCbor } },
            id = Guid.NewGuid().ToString()
        };

        string json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync("", content);
        string responseJson = await response.Content.ReadAsStringAsync();

        JsonElement result = JsonSerializer.Deserialize<JsonElement>(responseJson);

        if (result.TryGetProperty("error", out JsonElement error))
        {
            string message = error.GetProperty("message").GetString() ?? "Unknown error";
            int code = error.GetProperty("code").GetInt32();
            throw new OgmiosException(code, $"Transaction submission failed: {message}");
        }

        if (result.TryGetProperty("result", out JsonElement res) &&
            res.TryGetProperty("transaction", out JsonElement txResult) &&
            txResult.TryGetProperty("id", out JsonElement txId))
        {
            return txId.GetString() ?? throw new OgmiosException(0, "Could not parse transaction ID");
        }

        throw new OgmiosException(0, "Unexpected response format");
    }

    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address) =>
        throw new NotSupportedException("Use Kupo or Blockfrost for UTXO queries");

    public Task<ProtocolParams> GetParametersAsync() =>
        throw new NotSupportedException("Use Blockfrost or Ouroboros for protocol parameters");

    public Task<Metadata?> GetTransactionMetadataAsync(string txHash) =>
        throw new NotSupportedException("Use Blockfrost for metadata queries");
}

public class OgmiosException(int errorCode, string message) : Exception(message)
{
    public int ErrorCode { get; } = errorCode;
}
