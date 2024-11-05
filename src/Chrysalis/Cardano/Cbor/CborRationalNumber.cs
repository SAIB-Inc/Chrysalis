using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.RationalNumber)]
public record CborRationalNumber(ulong Numerator, ulong Denominator) : RawCbor;