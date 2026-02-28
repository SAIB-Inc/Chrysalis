using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

/// <summary>
/// Voting threshold parameters for stake pool operator governance actions.
/// </summary>
/// <param name="MotionNoConfidence">The threshold for a motion of no confidence.</param>
/// <param name="CommitteeNormal">The threshold for normal committee updates.</param>
/// <param name="CommitteeNoConfidence">The threshold for committee updates during no confidence.</param>
/// <param name="HardForkInitiation">The threshold for initiating a hard fork.</param>
/// <param name="SecurityVotingThreshold">The threshold for security-relevant parameter changes.</param>
[CborSerializable]
[CborList]
public partial record PoolVotingThresholds(
    [CborOrder(0)] CborRationalNumber MotionNoConfidence,
    [CborOrder(1)] CborRationalNumber CommitteeNormal,
    [CborOrder(2)] CborRationalNumber CommitteeNoConfidence,
    [CborOrder(3)] CborRationalNumber HardForkInitiation,
    [CborOrder(4)] CborRationalNumber SecurityVotingThreshold
) : CborBase;
