using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cardano.Minswap.Types.Common;

[CborConverter(typeof(UnionConverter))]
public record Bool: CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record False: Bool;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record True : Bool;