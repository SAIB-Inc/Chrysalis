// using Chrysalis.Cbor.Serialization.Attributes;
// using Chrysalis.Network.Cbor.Common;

// namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

// [CborSerializable]
// [CborUnion]
// public abstract record Acquire : LocalStateQueryMessage<Acquire>;

// public class AcquireTypes
// {
//     public static Acquire Default(Point? point) => point is not null ? SpecificPoint(point) : VolatileTip;

//     public static Acquire SpecificPoint(Point point) =>
//         new SpecificPoint(new ExactValue<CborInt>(new(0)), point);

//     public static Acquire VolatileTip =>
//         new VolatileTip(new ExactValue<CborInt>(new(8)));

//     public static Acquire ImmutableTip =>
//         new ImmutableTip(new ExactValue<CborInt>(new(10)));
// }

// [CborConverter(typeof(UnionConverter))]
// public abstract record AcquireIdx : CborBase;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record SpecificPoint(
//     [CborIndex(0)][ExactValue(0)] ExactValue<CborInt> Idx,
//     [CborIndex(1)] Point Point
// ) : Acquire;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record VolatileTip(
//     [CborIndex(0)][ExactValue(8)] ExactValue<CborInt> Idx
// ) : Acquire;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record ImmutableTip(
//     [CborIndex(0)][ExactValue(10)] ExactValue<CborInt> Idx
// ) : Acquire;

