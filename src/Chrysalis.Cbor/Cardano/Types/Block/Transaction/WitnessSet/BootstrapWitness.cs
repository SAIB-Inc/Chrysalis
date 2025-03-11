using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record BootstrapWitness(
    [CborIndex(0)] CborBytes PublicKey,
    [CborIndex(1)] CborBytes Signature,
    [CborIndex(2)] CborBytes ChainCode,
    [CborIndex(3)] CborBytes Attributes
) : CborBase;