using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

// [CborSerializable]
[CborList]
public partial record GovActionId(
    [CborIndex(0)] byte[] TransactionId,
    [CborIndex(1)] int GovActionIndex
) : CborBase<GovActionId>;