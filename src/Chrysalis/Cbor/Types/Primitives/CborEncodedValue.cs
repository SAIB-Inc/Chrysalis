using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(EncodedValueConverter))]
public record CborEncodedValue(byte[] Value) : CborBase;