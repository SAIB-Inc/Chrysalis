using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Jpg.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record Token(
    [CborIndex(0)]
    CborInt TokenType,

    [CborIndex(1)]
    CborMap<CborBytes, CborUlong> Amount
) : CborBase;