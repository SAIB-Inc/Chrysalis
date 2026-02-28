using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents the value portion of a Kupo UTxO match response.
/// </summary>
/// <param name="Coins">The lovelace amount.</param>
/// <param name="Assets">Optional dictionary of asset identifiers to quantities.</param>
public record KupoValue(
    [property: JsonPropertyName("coins")] long Coins,
    [property: JsonPropertyName("assets")] Dictionary<string, long>? Assets);

/// <summary>
/// Represents a script attached to a Kupo UTxO match.
/// </summary>
/// <param name="Language">The script language (native, plutus:v1, plutus:v2, plutus:v3).</param>
/// <param name="Script">The hex-encoded script bytes.</param>
public record KupoScript(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("script")] string Script);

/// <summary>
/// Represents the creation point of a Kupo UTxO match.
/// </summary>
/// <param name="SlotNo">The slot number where the UTxO was created.</param>
/// <param name="HeaderHash">The block header hash.</param>
public record KupoCreatedAt(
    [property: JsonPropertyName("slot_no")] long SlotNo,
    [property: JsonPropertyName("header_hash")] string HeaderHash);

/// <summary>
/// Represents the spending point of a Kupo UTxO match.
/// </summary>
/// <param name="SlotNo">The slot number where the UTxO was spent.</param>
/// <param name="HeaderHash">The block header hash.</param>
/// <param name="TransactionId">The spending transaction ID.</param>
/// <param name="InputIndex">The input index in the spending transaction.</param>
/// <param name="Redeemer">Optional redeemer data.</param>
public record KupoSpentAt(
    [property: JsonPropertyName("slot_no")] long SlotNo,
    [property: JsonPropertyName("header_hash")] string HeaderHash,
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("input_index")] int InputIndex,
    [property: JsonPropertyName("redeemer")] string? Redeemer);

/// <summary>
/// Represents a complete Kupo UTxO match response.
/// </summary>
/// <param name="TransactionIndex">The transaction index within the block.</param>
/// <param name="TransactionId">The transaction ID that created this UTxO.</param>
/// <param name="OutputIndex">The output index within the transaction.</param>
/// <param name="Address">The Bech32 address of the UTxO.</param>
/// <param name="Value">The value held by the UTxO.</param>
/// <param name="DatumHash">Optional datum hash.</param>
/// <param name="Datum">Optional inline datum hex.</param>
/// <param name="DatumType">The datum type (inline or hash).</param>
/// <param name="ScriptHash">Optional script hash.</param>
/// <param name="Script">Optional attached script.</param>
/// <param name="CreatedAt">When the UTxO was created.</param>
/// <param name="SpentAt">When the UTxO was spent, if applicable.</param>
public record KupoMatch(
    [property: JsonPropertyName("transaction_index")] int TransactionIndex,
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("output_index")] int OutputIndex,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("value")] KupoValue Value,
    [property: JsonPropertyName("datum_hash")] string? DatumHash,
    [property: JsonPropertyName("datum")] string? Datum,
    [property: JsonPropertyName("datum_type")] string? DatumType,
    [property: JsonPropertyName("script_hash")] string? ScriptHash,
    [property: JsonPropertyName("script")] KupoScript? Script,
    [property: JsonPropertyName("created_at")] KupoCreatedAt CreatedAt,
    [property: JsonPropertyName("spent_at")] KupoSpentAt? SpentAt);
