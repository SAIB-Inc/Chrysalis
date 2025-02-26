using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(UlongConverter))]
public record CborUlong(ulong Value) : CborBase, ICborNumber<ulong>;