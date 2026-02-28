using Chrysalis.Plutus.VM.Models.Enums;

namespace Chrysalis.Plutus.VM.Models;

/// <summary>
/// Represents the result of evaluating a Plutus script, including the redeemer tag, index, and execution units consumed.
/// </summary>
/// <param name="RedeemerTag">The tag identifying the redeemer purpose.</param>
/// <param name="Index">The index of the redeemer in the transaction.</param>
/// <param name="ExUnits">The execution units consumed by the script.</param>
public record EvaluationResult(RedeemerTag RedeemerTag, uint Index, ExUnits ExUnits);
