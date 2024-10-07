using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Protocol;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.List)]
public record Update(
    [CborProperty(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborProperty(1)] CborUlong Epoch
) : ICbor;