namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents a rational number as a numerator/denominator pair for precise fee calculations.
/// </summary>
/// <param name="Numerator">The numerator value.</param>
/// <param name="Denominator">The denominator value.</param>
public record RationalNumber(ulong Numerator, ulong Denominator);
