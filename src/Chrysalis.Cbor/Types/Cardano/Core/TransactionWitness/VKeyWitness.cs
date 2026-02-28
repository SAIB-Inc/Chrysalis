using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

/// <summary>
/// A verification key witness containing a public key and its signature over the transaction body hash.
/// </summary>
/// <param name="VKey">The verification (public) key bytes.</param>
/// <param name="Signature">The Ed25519 signature bytes.</param>
[CborSerializable]
[CborList]
public partial record VKeyWitness(
    [CborOrder(0)] ReadOnlyMemory<byte> VKey,
    [CborOrder(1)] ReadOnlyMemory<byte> Signature
) : CborBase, ICborPreserveRaw;
