using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Governance;

[CborSerializable(CborType.List)]
public record DRepVotingThresholds(
    [CborProperty(0)] CborRationalNumber MotionNoConfidence,
    [CborProperty(1)] CborRationalNumber CommitteeNormal,
    [CborProperty(2)] CborRationalNumber CommitteeNoConfidence,
    [CborProperty(4)] CborRationalNumber UpdateConstitution,
    [CborProperty(5)] CborRationalNumber HardForkInitiation,
    [CborProperty(6)] CborRationalNumber PpNetworkGroup,
    [CborProperty(7)] CborRationalNumber PpEconomicGroup,
    [CborProperty(8)] CborRationalNumber PpTechnicalGroup,
    [CborProperty(9)] CborRationalNumber PpGovernanceGroup,
    [CborProperty(10)] CborRationalNumber TreasuryWithdrawal
) : RawCbor;