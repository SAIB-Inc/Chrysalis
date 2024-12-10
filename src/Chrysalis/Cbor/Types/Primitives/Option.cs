using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Functional;

[CborConverter(typeof(UnionConverter))]
public abstract record Option<T> : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Some<T>([CborProperty(0)] T Value) : Option<T>;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record None<T> : Option<T>;