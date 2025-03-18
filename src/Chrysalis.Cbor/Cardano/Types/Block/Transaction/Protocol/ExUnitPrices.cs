using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborList]
public partial record ExUnitPrices(
    [CborIndex(0)] CborRationalNumber MemPrice,
    [CborIndex(1)] CborRationalNumber StepPrice
) : CborBase<ExUnitPrices>;
