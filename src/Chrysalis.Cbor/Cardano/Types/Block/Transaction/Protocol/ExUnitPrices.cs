using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborList]
public partial record ExUnitPrices(
    [CborOrder(0)] CborRationalNumber MemPrice,
    [CborOrder(1)] CborRationalNumber StepPrice
) : CborBase;
