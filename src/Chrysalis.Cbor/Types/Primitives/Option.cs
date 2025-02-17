using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(UnionConverter))]
public abstract record Option<T> : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record Some<T>([CborIndex(0)] T Value) : Option<T>;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record None<T> : Option<T>;