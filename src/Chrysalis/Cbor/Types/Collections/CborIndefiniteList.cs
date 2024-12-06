using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(ListConverter))]
public record CborIndefiniteList<T>(T[] Value) : CborMaybeIndefList<T> where T : CborBase;

[CborTag(258)]
public record CborIndefiniteListWithTag<T>(CborIndefiniteList<T> Value) : CborMaybeIndefList<T> where T : CborBase;