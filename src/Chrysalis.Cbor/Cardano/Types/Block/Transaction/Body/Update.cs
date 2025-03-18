using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborSerializable]
[CborList]
public partial record Update(
[CborIndex(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
[CborIndex(1)] UnhandledExceptionEventArgs Epoch
) : CborBase<Update>;