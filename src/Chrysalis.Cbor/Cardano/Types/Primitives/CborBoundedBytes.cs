using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Primitives;

[CborConverter(typeof(BytesConverter))]
[CborOptions(Size = 64, IsDefinite = true)]
public record CborBoundedBytes(byte[] Value) : CborBase;