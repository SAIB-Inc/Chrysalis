using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public readonly partial record struct PoolVotingThresholds : ICborType
{
    [CborOrder(0)] public partial CborRationalNumber MotionNoConfidence { get; }
    [CborOrder(1)] public partial CborRationalNumber CommitteeNormal { get; }
    [CborOrder(2)] public partial CborRationalNumber CommitteeNoConfidence { get; }
    [CborOrder(3)] public partial CborRationalNumber HardForkInitiation { get; }
    [CborOrder(4)] public partial CborRationalNumber SecurityRelevantThreshold { get; }
}
