using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Failure(
    [CborIndex(0)] [ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] FailureReason Reason
) : LocalStateQueryMessage;

[CborConverter(typeof(UnionConverter))]
public abstract record FailureReason : CborBase;

public record AcquireFailurePointTooOld([CborIndex(0)] [ExactValue(0)] CborInt Value) : ExactValue<CborInt>(Value);

[CborConverter(typeof(IntConverter))]
public record AcquireFailurePointNotOnChain([CborIndex(0)] [ExactValue(1)] CborInt Value) : ExactValue<CborInt>(Value);