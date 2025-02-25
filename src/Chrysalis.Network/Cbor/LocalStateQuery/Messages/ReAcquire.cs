using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(UnionConverter))]
public abstract record ReAcquire : LocalStateQueryMessage;

public class ReAcquireIdxs
{
    public static SpecificPoint SpecificPoint(Point point) =>
        new(new(6), point);

    public static VolatileTip VolatileTip() =>
        new(new(9));

    public static ImmutableTip ImmutableTip() =>
        new(new(11));
}

[CborConverter(typeof(UnionConverter))]
public abstract record ReAcquireIdx : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 6)]
public record ReAcquireSpecificPointIdx(int Value) : AcquireIdx;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 9)]
public record ReAcquireVolatileTipIdx(int Value) : AcquireIdx;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 11)]
public record ReAcquireImmutableTipIdx(int Value) : AcquireIdx;
