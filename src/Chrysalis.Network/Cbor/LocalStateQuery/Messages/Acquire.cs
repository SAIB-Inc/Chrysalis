using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(UnionConverter))]
public abstract record Acquire : LocalStateQueryMessage;

public class AcquireTypes
{
    public static Acquire Default(Point? point = null) => point is not null ? SpecificPoint(point) : VolatileTip;

    public static SpecificPoint SpecificPoint(Point point) =>
        new(new(0), point);

    public static VolatileTip VolatileTip =>
        new(new(8));

    public static ImmutableTip ImmutableTip =>
        new(new(10));
}

[CborConverter(typeof(UnionConverter))]
public abstract record AcquireIdx : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 0)]
public record SpecificPointIdx(int Value) : AcquireIdx;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 8)]
public record VolatileTipIdx(int Value) : AcquireIdx;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 10)]
public record ImmutableTipIdx(int Value) : AcquireIdx;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record SpecificPoint(
    [CborIndex(0)] SpecificPointIdx Idx,
    [CborIndex(0)] Point Point
) : Acquire;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record VolatileTip(
    [CborIndex(0)] VolatileTipIdx Idx
) : Acquire;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ImmutableTip(
    [CborIndex(0)] ImmutableTipIdx Idx
) : Acquire;

