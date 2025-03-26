using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public partial record Update(
    [CborOrder(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborOrder(1)] ulong Epoch
) : CborBase;