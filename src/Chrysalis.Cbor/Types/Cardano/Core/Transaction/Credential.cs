using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public partial record Credential(
    [CborOrder(0)] int CredentialType,
    [CborOrder(1)] byte[] Hash
) : CborBase;