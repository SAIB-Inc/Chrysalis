using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

// [CborSerializable]
[CborList]
public partial record Credential(
    [CborIndex(0)] int CredentialType,
    [CborIndex(1)] byte[] Hash
) : CborBase<Credential>;