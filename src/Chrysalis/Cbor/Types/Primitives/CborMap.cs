using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(MapConverter))]
public record CborMap<TKey, TValue>(Dictionary<TKey, TValue> Value) : CborBase
    where TKey : CborBase
    where TValue : CborBase;