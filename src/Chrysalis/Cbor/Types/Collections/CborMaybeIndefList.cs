using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(UnionConverter))]
public abstract record CborMaybeIndefList<T> : CborBase;





