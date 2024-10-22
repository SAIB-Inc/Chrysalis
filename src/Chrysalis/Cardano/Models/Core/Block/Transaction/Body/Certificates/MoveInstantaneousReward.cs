using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.Body.Certificates;

[CborSerializable(CborType.List)]
public record MoveInstantaneousReward(
    [CborProperty(0)] CborInt InstantaneousRewardSource,
    [CborProperty(1)] Target InstantaneousRewardTarget
) : RawCbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(StakeCredentials),
    typeof(OtherAccountingPot),
])]
public interface Target : ICbor;

public record StakeCredentials(Dictionary<Credential, CborInt> Value)
    : CborMap<Credential, CborInt>(Value), Target;

public record OtherAccountingPot(ulong Value)
    : CborUlong(Value), Target;
