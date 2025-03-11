using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true)]
public partial record CborBytes(byte[] Value) : CborBase;