using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record VKeyWitness(
    [CborIndex(0)] CborBytes VKey,
    [CborIndex(1)] CborBytes Signature
) : CborBase;