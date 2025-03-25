using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Primitives;

[CborSerializable]
[CborTag(30)]
[CborList]
public partial record CborRationalNumber(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
) : CborBase;
