using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

[CborSerializable]
[CborUnion]
public partial record ChainSyncMessage : CborBase;

[CborSerializable]
[CborUnion]
public partial record MessageNextResponse : ChainSyncMessage;

[CborSerializable]
[CborList]
public partial record MessageNextRequest(
    [CborOrder(0)] Value0 Idx
) : ChainSyncMessage;

[CborSerializable]
[CborList]
public partial record MessageAwaitReply(
   [CborOrder(0)] Value1 Idx
) : MessageNextResponse;

[CborSerializable]
[CborList]
public partial record MessageRollForward(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] CborEncodedValue Payload,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

[CborSerializable]
[CborList]
public partial record MessageRollBackward(
    [CborOrder(0)] Value3 Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

[CborSerializable]
[CborList]
public partial record MessageFindIntersect(
    [CborOrder(0)] Value4 Idx,
    [CborOrder(1)] Points Points
) : ChainSyncMessage;


[CborSerializable]
[CborUnion]
public partial record MessageIntersectResult : ChainSyncMessage;

[CborSerializable]
[CborList]
public partial record MessageIntersectFound(
    [CborOrder(0)] Value5 Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

[CborSerializable]
[CborList]
public partial record MessageIntersectNotFound(
    [CborOrder(0)] Value6 Idx,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

[CborSerializable]
[CborList]
public partial record MessageDone(
    [CborOrder(0)] Value7 Idx
) : ChainSyncMessage;


public static class ChainSyncMessages
{
    public static MessageNextRequest NextRequest()
    {
        return new(new Value0(0));
    }

    public static MessageFindIntersect FindIntersect(Points points)
    {
        return new(new Value4(4), points);
    }

    public static MessageDone Done()
    {
        return new(new Value7(7));
    }
}
