using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(RationalNumberConverter))]
[CborOptions(Tag = 30)]
public partial record CborRationalNumber(ulong Numerator, ulong Denominator) : CborBase;
