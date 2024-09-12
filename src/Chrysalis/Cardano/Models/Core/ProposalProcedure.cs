using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record ProposalProcedure(
    [CborProperty(0)] CborUlong Deposit,
    [CborProperty(1)] RewardAccount RewardAccount,
    [CborProperty(2)] GovAction GovAction, //@TODO   
    [CborProperty(3)] Anchor Anchor
) : ICbor;