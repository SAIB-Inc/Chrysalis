using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public partial record GovActionId(
    [CborOrder(0)] byte[] TransactionId,
    [CborOrder(1)] int GovActionIndex
) : CborBase;