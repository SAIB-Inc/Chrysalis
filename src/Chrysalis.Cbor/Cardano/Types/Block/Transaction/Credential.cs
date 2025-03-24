using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborSerializable]
[CborList]
public partial record Credential(
    [CborOrder(0)] int CredentialType,
    [CborOrder(1)] byte[] Hash
) : CborBase;