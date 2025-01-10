using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(LongConverter))]
public record CborLong(long Value) : CborBase;