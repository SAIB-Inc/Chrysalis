using Chrysalis.Plutus.VM.Models.Enums;

namespace Chrysalis.Plutus.VM.Models;

public record EvaluationResult(RedeemerTag RedeemerTag, uint Index, ExUnits ExUnits);