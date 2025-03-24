using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborUnion]
public abstract partial record ReAcquire : LocalStateQueryMessage;

public class ReAcquireIdxs
{
    public static ReAcquire Default(Point? point = null) => point is not null ? SpecificPoint(point) : VolatileTip;
    public static ReAcquireSpecificPoint SpecificPoint(Point point) => new(new Value6(6), point);
    public static ReAcquireVolatileTip VolatileTip => new(new Value9(9));
    public static ReAcquireImmutableTip ImmutableTip => new(new Value11(11));
}

[CborSerializable]
[CborUnion]
public abstract partial record ReAcquireIdx : CborBase;

[CborSerializable]
[CborList]
public partial record ReAcquireSpecificPoint(
    [CborOrder(0)] Value6 Idx,
    [CborOrder(1)] Point Point
) : ReAcquire;

[CborSerializable]
[CborUnion]
public partial record ReAcquireVolatileTip(
    [CborOrder(0)] Value9 Idx
) : ReAcquire;

[CborSerializable]
[CborUnion]
public partial record ReAcquireImmutableTip(
    [CborOrder(0)] Value11 Idx
) : ReAcquire;
