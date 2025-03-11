using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Custom;

[CborConverter(typeof(UnionConverter))]
public abstract partial record CborMaybeIndefList<T> : CborBase where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record CborDefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
public partial record CborIndefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true, Tag = 258)]
public partial record CborDefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = false, Tag = 258)]
public partial record CborIndefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;