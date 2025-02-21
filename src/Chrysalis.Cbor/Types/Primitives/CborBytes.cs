using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true)]
public record CborBytes(byte[] Value) : CborBase;