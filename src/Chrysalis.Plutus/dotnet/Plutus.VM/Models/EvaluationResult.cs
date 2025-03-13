using Plutus.VM.Models.Enums;

namespace Plutus.VM.Models;

public record EvaluationResult(RedeemerTag RedeemerTag, uint Index, ExUnits ExUnits);