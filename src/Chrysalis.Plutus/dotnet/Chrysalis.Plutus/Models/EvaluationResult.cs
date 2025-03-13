using Chrysalis.Plutus.Models.Enums;

namespace Chrysalis.Plutus.Models;

public record EvaluationResult(RedeemerTag RedeemerTag, uint Index, ExUnits ExUnits);