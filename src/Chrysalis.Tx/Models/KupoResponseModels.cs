using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

public record KupoValue(
    [property: JsonPropertyName("coins")] long Coins,
    [property: JsonPropertyName("assets")] Dictionary<string, long>? Assets);

public record KupoScript(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("script")] string Script);

public record KupoCreatedAt(
    [property: JsonPropertyName("slot_no")] long SlotNo,
    [property: JsonPropertyName("header_hash")] string HeaderHash);

public record KupoSpentAt(
    [property: JsonPropertyName("slot_no")] long SlotNo,
    [property: JsonPropertyName("header_hash")] string HeaderHash,
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("input_index")] int InputIndex,
    [property: JsonPropertyName("redeemer")] string? Redeemer);

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