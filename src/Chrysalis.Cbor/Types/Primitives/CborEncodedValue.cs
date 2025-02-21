using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(EncodedValueConverter))]
public record CborEncodedValue(byte[] Value) : CborBase;