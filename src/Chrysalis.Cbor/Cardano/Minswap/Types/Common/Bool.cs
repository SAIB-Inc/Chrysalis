using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Minswap.Types.Common;

[CborConverter(typeof(UnionConverter))]
public partial record Bool : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record False : Bool;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record True : Bool;