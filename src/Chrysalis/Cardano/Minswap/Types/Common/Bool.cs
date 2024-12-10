using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Minswap.Types.Common;

[CborConverter(typeof(UnionConverter))]
public record Bool: CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record False: Bool;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record True : Bool;