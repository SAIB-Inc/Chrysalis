using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborList]
public partial record VKeyWitness(
[CborIndex(0)] byte[] VKey,
[CborIndex(1)] byte[] Signature
) : CborBase<VKeyWitness>;