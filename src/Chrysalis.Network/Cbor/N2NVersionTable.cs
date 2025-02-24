using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor;

[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public record N2NVersionTable(Dictionary<N2NVersion, N2NVersionData> Value) : CborBase;