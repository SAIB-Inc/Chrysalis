using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Protocol;

[CborSerializable(CborType.List)]
public record ExUnitPrices(
    [CborProperty(0)] CborRationalNumber MemPrice,
    [CborProperty(1)] CborRationalNumber StepPrice
) : RawCbor;
