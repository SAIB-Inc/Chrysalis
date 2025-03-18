using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborList]
public partial record PoolVotingThresholds(
    [CborIndex(0)] CborRationalNumber MotionNoConfidence,
    [CborIndex(1)] CborRationalNumber CommitteeNormal,
    [CborIndex(2)] CborRationalNumber CommitteeNoConfidence,
    [CborIndex(3)] CborRationalNumber HardForkInitiation,
    [CborIndex(4)] CborRationalNumber SecurityVotingThreshold
) : CborBase<PoolVotingThresholds>;