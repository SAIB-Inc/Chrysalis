using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(ListConverter))]
public abstract record CborMaybeIndefList<T> : CborBase where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record CborDefList<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
public record CborIndefList<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborDefinite]
[CborTag(258)]
public record CborDefiniteListWithTag<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;


[CborConverter(typeof(ListConverter))]
[CborTag(258)]
public record CborIndefListWithTag<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;