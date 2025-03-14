using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
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
    [CborIndex(0)][ExactValue(0)] ExactValue<CborInt> Idx
) : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageAwaitReply(
   [CborIndex(0)][ExactValue(1)] ExactValue<CborInt> Idx
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageRollForward(
    [CborIndex(0)][ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborBytes Payload,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageRollBackward(
    [CborIndex(0)][ExactValue(3)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageFindIntersect(
    [CborIndex(0)][ExactValue(4)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Points Points
) : ChainSyncMessage;


[CborConverter(typeof(UnionConverter))]
public record MessageIntersectResult : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectFound(
    [CborIndex(0)][ExactValue(5)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageIntersectResult;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record MessageIntersectNotFound(
    [CborIndex(0)][ExactValue(6)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageIntersectResult;


public static class ChainSyncMessages
{
    public static MessageNextRequest NextRequest() =>
        new(new ExactValue<CborInt>(new(0)));
    public static MessageFindIntersect FindIntersect(Points points) =>
        new(new ExactValue<CborInt>(new(4)), points);
}
