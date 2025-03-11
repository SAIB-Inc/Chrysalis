using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(LongConverter))]
public partial record CborLong(long Value) : CborBase, ICborNumber<long>;