using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(RationalNumberConverter))]
public record CborRationalNumber(ulong Numerator, ulong Denominator) : CborBase;
