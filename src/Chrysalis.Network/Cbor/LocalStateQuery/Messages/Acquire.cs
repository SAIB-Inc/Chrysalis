using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborUnion]
public abstract partial record Acquire : LocalStateQueryMessage;

public static class AcquireTypes
{
    public static Acquire Default(Point? point)
    {
        return point is not null ? SpecificPoint(point) : VolatileTip;
    }

    public static Acquire SpecificPoint(Point point)
    {
        return new SpecificPoint(0, point);
    }

    public static Acquire VolatileTip => new VolatileTip(8);
    public static Acquire ImmutableTip => new ImmutableTip(10);
}

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record SpecificPoint(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point
) : Acquire;

[CborSerializable]
[CborList]
[CborIndex(8)]
public partial record VolatileTip(
    [CborOrder(0)] int Idx
) : Acquire;

[CborSerializable]
[CborList]
[CborIndex(10)]
public partial record ImmutableTip(
    [CborOrder(0)] int Idx
) : Acquire;
