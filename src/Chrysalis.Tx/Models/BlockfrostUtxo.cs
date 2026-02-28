using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents a UTxO response from the Blockfrost API.
/// </summary>
public class BlockfrostUtxo
{
    /// <summary>
    /// Gets or sets the Bech32 address.
    /// </summary>
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the transaction hash.
    /// </summary>
    [JsonPropertyName("tx_hash")]
    public string? TxHash { get; set; }

    /// <summary>
    /// Gets or sets the transaction index.
    /// </summary>
    [JsonPropertyName("tx_index")]
    public int? TxIndex { get; set; }

    /// <summary>
    /// Gets or sets the output index.
    /// </summary>
    [JsonPropertyName("output_index")]
    public int? OutputIndex { get; set; }

    /// <summary>
    /// Gets or sets the list of amounts (lovelace and native assets).
    /// </summary>
    [JsonPropertyName("amount")]
    public List<Amount>? Amount { get; init; }

    /// <summary>
    /// Gets or sets the block hash.
    /// </summary>
    [JsonPropertyName("block")]
    public string? Block { get; set; }

    /// <summary>
    /// Gets or sets the datum hash.
    /// </summary>
    [JsonPropertyName("data_hash")]
    public string? DataHash { get; set; }

    /// <summary>
    /// Gets or sets the inline datum hex string.
    /// </summary>
    [JsonPropertyName("inline_datum")]
    public string? InlineDatum { get; set; }

    /// <summary>
    /// Gets or sets the reference script hash.
    /// </summary>
    [JsonPropertyName("reference_script_hash")]
    public string? ReferenceScriptHash { get; set; }

}

/// <summary>
/// Represents an amount entry in a Blockfrost UTxO response.
/// </summary>
public class Amount
{
    /// <summary>
    /// Gets or sets the unit identifier (lovelace or policy+asset hex).
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// Gets or sets the quantity as a string.
    /// </summary>
    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; }
}
