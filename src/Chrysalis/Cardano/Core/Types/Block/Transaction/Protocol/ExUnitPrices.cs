using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;

[CborConverter(typeof(CustomListConverter))]
public record ExUnitPrices(
    [CborProperty(0)] CborRationalNumber MemPrice,
    [CborProperty(1)] CborRationalNumber StepPrice
) : CborBase;
