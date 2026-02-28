using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

/// <summary>
/// A bootstrap (Byron-era) witness containing a public key, signature, chain code, and attributes.
/// </summary>
/// <param name="PublicKey">The public key bytes.</param>
/// <param name="Signature">The signature bytes.</param>
/// <param name="ChainCode">The chain code for HD key derivation.</param>
/// <param name="Attributes">The Byron address attributes.</param>
[CborSerializable]
[CborList]
public partial record BootstrapWitness(
   [CborOrder(0)] byte[] PublicKey,
   [CborOrder(1)] byte[] Signature,
   [CborOrder(2)] byte[] ChainCode,
   [CborOrder(3)] byte[] Attributes
) : CborBase, ICborPreserveRaw;
