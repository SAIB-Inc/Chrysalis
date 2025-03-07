
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(NullableConverter))]
public record CborNullable<T>(T? Value) : CborBase where T : CborBase;