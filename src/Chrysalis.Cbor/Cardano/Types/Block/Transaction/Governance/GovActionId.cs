using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record GovActionId(
    [CborOrder(0)] byte[] TransactionId,
    [CborOrder(1)] int GovActionIndex
) : CborBase<GovActionId>;