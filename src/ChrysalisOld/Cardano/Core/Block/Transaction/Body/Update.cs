using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Update(
    [CborProperty(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborProperty(1)] CborUlong Epoch
) : RawCbor;