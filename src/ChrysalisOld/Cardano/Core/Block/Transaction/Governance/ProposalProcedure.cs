using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record ProposalProcedure(
    [CborProperty(0)] CborUlong Deposit,
    [CborProperty(1)] RewardAccount RewardAccount,
    [CborProperty(2)] GovAction GovAction, 
    [CborProperty(3)] Anchor Anchor
) : RawCbor;