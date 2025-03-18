using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record ProposalProcedure(
    [CborOrder(0)] ulong Deposit,
    [CborOrder(1)] RewardAccount RewardAccount,
    [CborOrder(2)] GovAction GovAction,
    [CborOrder(3)] Anchor Anchor
) : CborBase<ProposalProcedure>;