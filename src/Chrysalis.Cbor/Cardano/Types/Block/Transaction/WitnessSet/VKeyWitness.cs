using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborList]
public partial record VKeyWitness(
    [CborOrder(0)] byte[] VKey,
    [CborOrder(1)] byte[] Signature
) : CborBase<VKeyWitness>;