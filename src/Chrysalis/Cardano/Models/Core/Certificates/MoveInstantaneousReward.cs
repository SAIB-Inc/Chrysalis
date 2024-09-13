using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Certificates;

[CborSerializable(CborType.List)]
public record MoveInstantaneousReward(
    [CborProperty(0)] CborInt InstantaneousRewardSource,
    [CborProperty(1)] Target InstantaneousRewardTarget
) : ICbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(StakeCredentials),
    typeof(CborUlong),
])]
public record Target : ICbor;

public record StakeCredentials(
    CborMap<Credential, CborInt> Value
) : Target;
