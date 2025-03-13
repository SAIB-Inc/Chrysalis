using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;

namespace Chrysalis.Cbor.Types.Primitives;

[CborSerializable]
[CborTag(30)]
public partial record CborRationalNumber(ulong Numerator, ulong Denominator) : CborBase<CborRationalNumber>;
