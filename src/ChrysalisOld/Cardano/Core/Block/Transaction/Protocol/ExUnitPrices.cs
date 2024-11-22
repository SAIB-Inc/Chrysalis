using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record ExUnitPrices(
    [CborProperty(0)] CborRationalNumber MemPrice,
    [CborProperty(1)] CborRationalNumber StepPrice
) : RawCbor;
