
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(CustomListConverter))]
[CborDefinite]
public record BootstrapWitness(
    [CborProperty(0)] CborBytes PublicKey,
    [CborProperty(1)] CborBytes Signature,
    [CborProperty(2)] CborBytes ChainCode,
    [CborProperty(3)] CborBytes Attributes
) : CborBase;