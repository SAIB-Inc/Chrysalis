using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborUnion]
public abstract partial record ReAcquire : LocalStateQueryMessage;

public static class ReAcquireIdxs
{
    public static ReAcquire Default(Point? point = null)
    {
        return point is not null ? SpecificPoint(point) : VolatileTip;
    }

    public static ReAcquireSpecificPoint SpecificPoint(Point point)
    {
        return new(6, point);
    }

    public static ReAcquireVolatileTip VolatileTip => new(9);
    public static ReAcquireImmutableTip ImmutableTip => new(11);
}

[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record ReAcquireSpecificPoint(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point
) : ReAcquire;

[CborSerializable]
[CborList]
[CborIndex(9)]
public partial record ReAcquireVolatileTip(
    [CborOrder(0)] int Idx
) : ReAcquire;

[CborSerializable]
[CborList]
[CborIndex(11)]
public partial record ReAcquireImmutableTip(
    [CborOrder(0)] int Idx
) : ReAcquire;
