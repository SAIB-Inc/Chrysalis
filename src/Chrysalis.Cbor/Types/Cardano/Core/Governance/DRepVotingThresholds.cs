using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

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
