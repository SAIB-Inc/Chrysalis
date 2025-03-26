using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public partial record ExUnitPrices(
    [CborOrder(0)] CborRationalNumber MemPrice,
    [CborOrder(1)] CborRationalNumber StepPrice
) : CborBase;
