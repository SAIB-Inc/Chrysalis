using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(EncodedValueConverter))]
public partial record CborEncodedValue(byte[] Value) : CborBase;