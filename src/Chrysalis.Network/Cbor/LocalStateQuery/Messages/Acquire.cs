using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(UnionConverter))]
public abstract record Acquire : LocalStateQueryMessage;

public class AcquireTypes
{
    public static Acquire Default(Point? point = null) => point is not null ? SpecificPoint(point) : VolatileTip;

    public static SpecificPoint SpecificPoint(Point point) =>
        new (new ExactValue<CborInt>(new(0)), point);

    public static VolatileTip VolatileTip =>
        new (new ExactValue<CborInt>(new(8)));

    public static ImmutableTip ImmutableTip =>
        new (new ExactValue<CborInt>(new(10)));
}

[CborConverter(typeof(UnionConverter))]
public abstract record AcquireIdx : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record SpecificPoint(
    [CborIndex(0)] [ExactValue(0)] ExactValue<CborInt> Idx,
    [CborIndex(1)] Point Point
) : Acquire;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record VolatileTip(
    [CborIndex(0)] [ExactValue(8)] ExactValue<CborInt> Idx
) : Acquire;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ImmutableTip(
    [CborIndex(0)] [ExactValue(10)] ExactValue<CborInt> Idx
) : Acquire;

