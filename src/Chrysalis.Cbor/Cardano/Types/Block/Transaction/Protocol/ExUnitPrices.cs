using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborConverter(typeof(CustomListConverter))]
public partial record ExUnitPrices(
    [CborIndex(0)] CborRationalNumber MemPrice,
    [CborIndex(1)] CborRationalNumber StepPrice
) : CborBase;
