using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Body;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Governance;

[CborSerializable(CborType.List)]
public record ProposalProcedure(
    [CborProperty(0)] CborUlong Deposit,
    [CborProperty(1)] RewardAccount RewardAccount,
    [CborProperty(2)] GovAction GovAction, 
    [CborProperty(3)] Anchor Anchor
) : RawCbor;