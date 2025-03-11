using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(MapConverter))]
public partial record CborMap<TKey, TValue>(Dictionary<TKey, TValue> Value) : CborBase
    where TKey : CborBase
    where TValue : CborBase;