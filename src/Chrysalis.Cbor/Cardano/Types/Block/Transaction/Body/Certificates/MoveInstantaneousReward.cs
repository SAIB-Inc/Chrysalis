using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(CustomListConverter))]
public record MoveInstantaneousReward(
    [CborIndex(0)] CborInt InstantaneousRewardSource,
    [CborIndex(1)] Target InstantaneousRewardTarget
) : CborBase;


[CborConverter(typeof(UnionConverter))]
public abstract record Target : CborBase;


[CborConverter(typeof(MapConverter))]
public record StakeCredentials(Dictionary<Credential, CborUlong> Value) : Target;


[CborConverter(typeof(UlongConverter))]
public record OtherAccountingPot(ulong Value) : Target;
