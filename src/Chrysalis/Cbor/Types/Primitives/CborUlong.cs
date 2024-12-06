using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(UlongConverter))]
public record CborUlong(ulong Value) : CborBase;