using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// A governance proposal procedure containing the deposit, return address, action, and metadata anchor.
/// </summary>
/// <param name="Deposit">The deposit amount in lovelace required for the proposal.</param>
/// <param name="RewardAccount">The reward account to receive the deposit refund.</param>
/// <param name="GovAction">The governance action being proposed.</param>
/// <param name="Anchor">The metadata anchor providing additional proposal details.</param>
[CborSerializable]
[CborList]
public partial record ProposalProcedure(
    [CborOrder(0)] ulong Deposit,
    [CborOrder(1)] RewardAccount RewardAccount,
    [CborOrder(2)] GovAction GovAction,
    [CborOrder(3)] Anchor Anchor
) : CborBase;
