using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborList]
public partial record BootstrapWitness(
   [CborOrder(0)] byte[] PublicKey,
   [CborOrder(1)] byte[] Signature,
   [CborOrder(2)] byte[] ChainCode,
   [CborOrder(3)] byte[] Attributes
) : CborBase, ICborPreserveRaw;