using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(UnionConverter))]
public abstract record CborMaybeIndefList<T> : CborBase where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record CborDefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
public record CborIndefList<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborDefinite]
[CborTag(258)]
public record CborDefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborTag(258)]
public record CborIndefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T> where T : CborBase;