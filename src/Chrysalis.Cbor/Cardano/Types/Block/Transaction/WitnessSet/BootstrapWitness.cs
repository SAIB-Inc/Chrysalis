using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborList]
public partial record BootstrapWitness(
   [CborIndex(0)] byte[] PublicKey,
   [CborIndex(1)] byte[] Signature,
   [CborIndex(2)] byte[] ChainCode,
   [CborIndex(3)] byte[] Attributes
) : CborBase<BootstrapWitness>;