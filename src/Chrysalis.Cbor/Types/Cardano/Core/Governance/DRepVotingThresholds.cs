using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Voting threshold parameters for delegated representative (DRep) governance actions.
/// </summary>
/// <param name="MotionNoConfidence">The threshold for a motion of no confidence.</param>
/// <param name="CommitteeNormal">The threshold for normal committee updates.</param>
/// <param name="CommitteeNoConfidence">The threshold for committee updates during no confidence.</param>
/// <param name="UpdateConstitution">The threshold for updating the constitution.</param>
/// <param name="HardForkInitiation">The threshold for initiating a hard fork.</param>
/// <param name="PpNetworkGroup">The threshold for network group protocol parameter changes.</param>
/// <param name="PpEconomicGroup">The threshold for economic group protocol parameter changes.</param>
/// <param name="PpTechnicalGroup">The threshold for technical group protocol parameter changes.</param>
/// <param name="PpGovernanceGroup">The threshold for governance group protocol parameter changes.</param>
/// <param name="TreasuryWithdrawal">The threshold for treasury withdrawals.</param>
[CborSerializable]
[CborList]
public partial record DRepVotingThresholds(
    [CborOrder(0)] CborRationalNumber MotionNoConfidence,
    [CborOrder(1)] CborRationalNumber CommitteeNormal,
    [CborOrder(2)] CborRationalNumber CommitteeNoConfidence,
    [CborOrder(4)] CborRationalNumber UpdateConstitution,
    [CborOrder(5)] CborRationalNumber HardForkInitiation,
    [CborOrder(6)] CborRationalNumber PpNetworkGroup,
    [CborOrder(7)] CborRationalNumber PpEconomicGroup,
    [CborOrder(8)] CborRationalNumber PpTechnicalGroup,
    [CborOrder(9)] CborRationalNumber PpGovernanceGroup,
    [CborOrder(10)] CborRationalNumber TreasuryWithdrawal
) : CborBase;
