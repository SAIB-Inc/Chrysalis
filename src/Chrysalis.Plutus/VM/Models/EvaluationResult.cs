namespace Chrysalis.Plutus.VM.Models;

/// <summary>
/// Result of evaluating a single redeemer in a transaction.
/// Contains the redeemer tag, index, and computed execution units.
/// </summary>
public sealed record EvaluationResult(int RedeemerTag, ulong Index, ExUnitsResult ExUnits);

/// <summary>
/// Execution units consumed by a script evaluation.
/// Memory and CPU steps as unsigned 64-bit values for consumer compatibility.
/// </summary>
public sealed record ExUnitsResult(ulong Mem, ulong Steps);
