using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// A Cardano credential consisting of a type tag and a hash, used for payment and staking credentials.
/// </summary>
/// <param name="CredentialType">The credential type (0 = key hash, 1 = script hash).</param>
/// <param name="Hash">The credential hash (address key hash or script hash).</param>
[CborSerializable]
[CborList]
public partial record Credential(
    [CborOrder(0)] int CredentialType,
    [CborOrder(1)] byte[] Hash
) : CborBase;
