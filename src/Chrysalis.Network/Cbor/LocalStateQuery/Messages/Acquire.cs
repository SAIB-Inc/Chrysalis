using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborUnion]
public abstract partial record Acquire : LocalStateQueryMessage;

public class AcquireTypes
{
    public static Acquire Default(Point? point) => point is not null ? SpecificPoint(point) : VolatileTip;
    public static Acquire SpecificPoint(Point point) => new SpecificPoint(new Value0(0), point);
    public static Acquire VolatileTip => new VolatileTip(new Value8(8));
    public static Acquire ImmutableTip => new ImmutableTip(new Value10(10));
}

[CborSerializable]
[CborUnion]
public abstract partial record AcquireIdx : CborBase;

[CborSerializable]
[CborList]
public partial record SpecificPoint(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] Point Point
) : Acquire;

[CborSerializable]
[CborList]
public partial record VolatileTip(
    [CborOrder(0)] Value8 Idx
) : Acquire;

[CborSerializable]
[CborList]
public partial record ImmutableTip(
    [CborOrder(0)] Value10 Idx
) : Acquire;

