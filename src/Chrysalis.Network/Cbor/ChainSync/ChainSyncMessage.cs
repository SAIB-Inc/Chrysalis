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
[CborIndex(0)]
public partial record MessageNextRequest(
    [CborOrder(0)] int Idx
) : ChainSyncMessage;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record MessageAwaitReply(
   [CborOrder(0)] int Idx
) : MessageNextResponse;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record MessageRollForward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue Payload,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record MessageRollBackward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record MessageFindIntersect(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Points Points
) : ChainSyncMessage;


[CborSerializable]
[CborUnion]
public partial record MessageIntersectResult : ChainSyncMessage;

[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record MessageIntersectFound(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record MessageIntersectNotFound(
    [CborOrder(0)] int Idx,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record MessageDone(
    [CborOrder(0)] int Idx
) : ChainSyncMessage;


public static class ChainSyncMessages
{
    public static MessageNextRequest NextRequest()
    {
        return new(0);
    }

    public static MessageFindIntersect FindIntersect(Points points)
    {
        return new(4, points);
    }

    public static MessageDone Done()
    {
        return new(7);
    }
}
