// using Chrysalis.Cbor.Attributes;

// using Chrysalis.Cbor.Types;
// using Chrysalis.Cbor.Types.Custom;
// using Chrysalis.Cbor.Types.Primitives;
// using Chrysalis.Network.Cbor.Common;

// namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

// [CborConverter(typeof(UnionConverter))]
// public abstract record ReAcquire : LocalStateQueryMessage;

// public class ReAcquireIdxs
// {
//     public static ReAcquire Default(Point? point = null) => point is not null ? SpecificPoint(point) : VolatileTip;

//     public static ReAcquireSpecificPoint SpecificPoint(Point point) =>
//         new(new ExactValue<CborInt>(new(6)), point);

//     public static ReAcquireVolatileTip VolatileTip =>
//         new(new ExactValue<CborInt>(new(9)));

//     public static ReAcquireImmutableTip ImmutableTip =>
//         new(new ExactValue<CborInt>(new(11)));
// }

// [CborConverter(typeof(UnionConverter))]
// public abstract record ReAcquireIdx : CborBase;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record ReAcquireSpecificPoint(
//     [CborIndex(0)][ExactValue(6)] ExactValue<CborInt> Idx,
//     [CborIndex(1)] Point Point
// ) : ReAcquire;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record ReAcquireVolatileTip(
//     [CborIndex(0)][ExactValue(9)] ExactValue<CborInt> Idx
// ) : ReAcquire;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record ReAcquireImmutableTip(
//     [CborIndex(0)][ExactValue(11)] ExactValue<CborInt> Idx
// ) : ReAcquire;
