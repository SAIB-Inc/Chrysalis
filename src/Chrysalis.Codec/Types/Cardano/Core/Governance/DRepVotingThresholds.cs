using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct DRepVotingThresholds : ICborType
{
    [CborOrder(0)] public partial CborRationalNumber MotionNoConfidence { get; }
    [CborOrder(1)] public partial CborRationalNumber CommitteeNormal { get; }
    [CborOrder(2)] public partial CborRationalNumber CommitteeNoConfidence { get; }
    [CborOrder(3)] public partial CborRationalNumber UpdateConstitution { get; }
    [CborOrder(4)] public partial CborRationalNumber HardForkInitiation { get; }
    [CborOrder(5)] public partial CborRationalNumber PPNetworkGroup { get; }
    [CborOrder(6)] public partial CborRationalNumber PPEconomicGroup { get; }
    [CborOrder(7)] public partial CborRationalNumber PPTechnicalGroup { get; }
    [CborOrder(8)] public partial CborRationalNumber PPGovernanceGroup { get; }
    [CborOrder(9)] public partial CborRationalNumber TreasuryWithdrawal { get; }
}
