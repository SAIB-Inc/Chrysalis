using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record CborDefiniteList<T>(List<T> Value) : CborBase where T : CborBase;
