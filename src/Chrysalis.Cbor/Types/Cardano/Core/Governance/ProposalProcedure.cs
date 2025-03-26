using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public partial record ProposalProcedure(
    [CborOrder(0)] ulong Deposit,
    [CborOrder(1)] RewardAccount RewardAccount,
    [CborOrder(2)] GovAction GovAction,
    [CborOrder(3)] Anchor Anchor
) : CborBase;