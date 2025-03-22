using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborSerializable]
[CborList]
public partial record Update(
    [CborOrder(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborOrder(1)] ulong Epoch
) : CborBase<Update>;