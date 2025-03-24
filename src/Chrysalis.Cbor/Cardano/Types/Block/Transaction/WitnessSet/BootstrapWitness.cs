using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborList]
public partial record BootstrapWitness(
   [CborOrder(0)] byte[] PublicKey,
   [CborOrder(1)] byte[] Signature,
   [CborOrder(2)] byte[] ChainCode,
   [CborOrder(3)] byte[] Attributes
) : CborBase, ICborPreserveRaw;