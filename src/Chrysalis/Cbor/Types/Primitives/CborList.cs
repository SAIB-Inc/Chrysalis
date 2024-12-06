using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Collections;

[CborConverter(typeof(ListConverter))]
public record CborList<T>(List<T> Value) : CborBase where T : CborBase;
