using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record PoolVotingThresholds(
    [CborProperty(0)] CborRationalNumber MotionNoConfidence,
    [CborProperty(1)] CborRationalNumber CommitteeNormal,
    [CborProperty(2)] CborRationalNumber CommitteeNoConfidence,
    [CborProperty(3)] CborRationalNumber HardForkInitiation,
    [CborProperty(4)] CborRationalNumber SecurityVotingThreshold
) : RawCbor;