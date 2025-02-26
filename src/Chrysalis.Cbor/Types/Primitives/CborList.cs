using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(ListConverter))]
public record CborList<T>(List<T> Value) : CborBase where T : CborBase;

