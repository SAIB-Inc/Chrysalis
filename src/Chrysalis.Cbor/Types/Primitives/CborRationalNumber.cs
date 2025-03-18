using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Primitives;

// [CborSerializable]
[CborTag(30)]
public partial record CborRationalNumber(ulong Numerator, ulong Denominator) : CborBase<CborRationalNumber>;
