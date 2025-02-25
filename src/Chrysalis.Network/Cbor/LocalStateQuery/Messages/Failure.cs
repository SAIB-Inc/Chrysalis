using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public record Failure(
    [CborIndex(0)] FailureIdx Idx,
    [CborIndex(1)] FailureReason Reason
) : LocalStateQueryMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 2)]
public record FailureIdx(int Value) : CborBase;

[CborConverter(typeof(UnionConverter))]
public abstract record FailureReason : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record AcquireFailurePointTooOld(int Value) : FailureReason;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record AcquireFailurePointNotOnChain(int Value) : FailureReason;