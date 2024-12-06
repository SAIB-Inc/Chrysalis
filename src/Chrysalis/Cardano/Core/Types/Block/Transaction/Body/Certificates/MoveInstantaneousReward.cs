using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(CustomListConverter))]
public record MoveInstantaneousReward(
    [CborProperty(0)] CborInt InstantaneousRewardSource,
    [CborProperty(1)] Target InstantaneousRewardTarget
) : CborBase;


[CborConverter(typeof(UnionConverter))]
public abstract record Target : CborBase;


[CborConverter(typeof(MapConverter))]
public record StakeCredentials(Dictionary<Credential, CborInt> Value) : CborBase;


[CborConverter(typeof(UlongConverter))]
public record OtherAccountingPot(ulong Value) : Target;
