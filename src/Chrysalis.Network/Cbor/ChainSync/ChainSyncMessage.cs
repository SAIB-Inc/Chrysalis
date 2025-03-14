using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Header;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

[CborConverter(typeof(UnionConverter))]
public partial record ChainSyncMessage : CborBase;

[CborConverter(typeof(UnionConverter))]
public partial record MessageNextResponse : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageNextRequest(
    [CborIndex(0)][ExactValue(0)] ExactValue<CborInt> Idx
) : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageAwaitReply(
   [CborIndex(0)][ExactValue(1)] ExactValue<CborInt> Idx
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageRollForward(
    [CborIndex(0)][ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborBytes Payload,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageRollBackward(
    [CborIndex(0)][ExactValue(3)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageNextResponse;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageFindIntersect(
    [CborIndex(0)][ExactValue(4)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Points Points
) : ChainSyncMessage;


[CborConverter(typeof(UnionConverter))]
public partial record MessageIntersectResult : ChainSyncMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageIntersectFound(
    [CborIndex(0)][ExactValue(5)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point,
    [CborIndex(2)] Tip Tip
) : MessageIntersectResult;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MessageIntersectNotFound(
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
