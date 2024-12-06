using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record CborDefiniteList<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;

[CborTag(258)]
public record CborDefiniteListWithTag<T>(CborDefiniteList<T> Value) : CborMaybeIndefList<T> where T : CborBase;