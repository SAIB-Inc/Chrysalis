using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

/// <summary>
/// Represents a move instantaneous reward containing the source and target.
/// </summary>
/// <param name="InstantaneousRewardSource">The source of the instantaneous reward (0 = reserves, 1 = treasury).</param>
/// <param name="InstantaneousRewardTarget">The target for the instantaneous reward distribution.</param>
[CborSerializable]
[CborList]
public partial record MoveInstantaneousReward(
    [CborOrder(0)] int InstantaneousRewardSource,
    [CborOrder(1)] Target InstantaneousRewardTarget
) : CborBase;

/// <summary>
/// Represents the target of a move instantaneous reward.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Target : CborBase { }

/// <summary>
/// Represents a reward target distributing to specific stake credentials.
/// </summary>
/// <param name="Value">The dictionary mapping stake credentials to reward amounts in lovelace.</param>
[CborSerializable]
public partial record StakeCredentials(Dictionary<Credential, ulong> Value) : Target;

/// <summary>
/// Represents a reward target transferring to another accounting pot.
/// </summary>
/// <param name="Value">The amount in lovelace to transfer.</param>
[CborSerializable]
public partial record OtherAccountingPot(ulong Value) : Target;
