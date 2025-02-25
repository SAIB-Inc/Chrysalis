using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Header;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

[CborConverter(typeof(UnionConverter))]
public record ChainSyncMessage : CborBase;

[CborConverter(typeof(UnionConverter))]
public record MessageNextResponse : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageNextRequest(
    [CborIndex(0)] MessageNextRequestIdx Idx
) : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageAwaitReply(
    [CborIndex(0)] MessageAwaitReplyIdx Idx
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageRollForward(
    [CborIndex(0)] MessageRollForwardIdx Idx,
    [CborIndex(1)] CborEncodedValue Payload,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageRollBackward(
    [CborIndex(0)] MessageRollBackwardIdx Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageFindIntersect(
    [CborIndex(0)] MessageFindIntersectIdx Idx,
    [CborIndex(1)] Points Points
) : ChainSyncMessage;


[CborConverter(typeof(UnionConverter))]
public record MessageIntersectResult : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectFound(
    [CborIndex(0)] MessageIntersectFoundIdx Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageIntersectResult;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectNotFound(
    [CborIndex(0)] MessageIntersectNotFoundIdx Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageIntersectResult;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record MessageNextRequestIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 1)]
public record MessageAwaitReplyIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 2)]
public record MessageRollForwardIdx(int Value) : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 3)]
public record MessageRollBackwardIdx(int Value) : CborBase;

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
    public static MessageNextRequest NextRequest() =>
        new(new MessageNextRequestIdx(0));
    public static MessageFindIntersect FindIntersect(Points points) =>
        new(new MessageFindIntersectIdx(4), points);
}
