using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(IntConverter))]
public partial record CborInt(int Value) : CborBase, ICborNumber<int>;