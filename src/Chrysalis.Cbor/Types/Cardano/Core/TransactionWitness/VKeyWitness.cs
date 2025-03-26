using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborList]
public partial record VKeyWitness(
    [CborOrder(0)] byte[] VKey,
    [CborOrder(1)] byte[] Signature
) : CborBase, ICborPreserveRaw;