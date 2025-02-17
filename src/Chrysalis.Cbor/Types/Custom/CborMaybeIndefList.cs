using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Custom;

[CborConverter(typeof(UnionConverter))]
public abstract record CborMaybeIndefList<T> : CborBase where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public record CborDefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
public record CborIndefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true, Tag = 258)]
public record CborDefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = false, Tag = 258)]
public record CborIndefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;