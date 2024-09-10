using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.RationalNumber)]
public record CborRationalNumber(ulong Numerator, ulong Denominator) : ICbor;