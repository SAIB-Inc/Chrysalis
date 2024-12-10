using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;

[CborConverter(typeof(CustomListConverter))]
public record PoolVotingThresholds(
    [CborProperty(0)] CborRationalNumber MotionNoConfidence,
    [CborProperty(1)] CborRationalNumber CommitteeNormal,
    [CborProperty(2)] CborRationalNumber CommitteeNoConfidence,
    [CborProperty(3)] CborRationalNumber HardForkInitiation,
    [CborProperty(4)] CborRationalNumber SecurityVotingThreshold
) : CborBase;