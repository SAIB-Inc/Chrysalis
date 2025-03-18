using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record ProposalProcedure(
    [CborIndex(0)] ulong Deposit,
    [CborIndex(1)] RewardAccount RewardAccount,
    [CborIndex(2)] GovAction GovAction,
    [CborIndex(3)] Anchor Anchor
) : CborBase<ProposalProcedure>;