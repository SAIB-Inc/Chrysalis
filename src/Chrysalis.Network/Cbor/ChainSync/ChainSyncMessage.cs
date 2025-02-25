using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

[CborConverter(typeof(UnionConverter))]
public record ChainSyncMessage : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageFindIntersect(
    [CborIndex(0)] MessageFindIntersectIdx Idx,
    [CborIndex(1)] Points Points
) : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectFound(
    [CborIndex(0)] MessageIntersectFoundIdx Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectNotFound(
    [CborIndex(0)] MessageIntersectNotFoundIdx Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : ChainSyncMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record MessageRequestNextIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record MessageAwaitReplyIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 4)]
public record MessageFindIntersectIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 5)]
public record MessageIntersectFoundIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 6)]
public record MessageIntersectNotFoundIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 7)]
public record MessageDoneIdx(int Value) : CborBase;

public static class ChainSyncMessages
{
    public static MessageFindIntersect FindIntersect(Points points) =>
        new(new MessageFindIntersectIdx(4), points);
}
