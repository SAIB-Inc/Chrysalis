using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public partial record PoolVotingThresholds(
    [CborOrder(0)] CborRationalNumber MotionNoConfidence,
    [CborOrder(1)] CborRationalNumber CommitteeNormal,
    [CborOrder(2)] CborRationalNumber CommitteeNoConfidence,
    [CborOrder(3)] CborRationalNumber HardForkInitiation,
    [CborOrder(4)] CborRationalNumber SecurityVotingThreshold
) : CborBase;