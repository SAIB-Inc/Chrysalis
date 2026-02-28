using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// A CBOR-encoded rational number represented as a numerator/denominator pair with tag 30.
/// </summary>
/// <param name="Numerator">The numerator of the rational number.</param>
/// <param name="Denominator">The denominator of the rational number.</param>
[CborSerializable]
[CborTag(30)]
[CborList]
public partial record CborRationalNumber(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
) : CborBase;
