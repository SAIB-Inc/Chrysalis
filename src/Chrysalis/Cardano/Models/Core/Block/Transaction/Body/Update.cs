using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Protocol;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Body;

[CborSerializable(CborType.List)]
public record Update(
    [CborProperty(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborProperty(1)] CborUlong Epoch
) : RawCbor;