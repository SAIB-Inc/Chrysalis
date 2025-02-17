using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Jpg.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Token(
    [CborProperty(0)]
    CborInt TokenType,

    [CborProperty(1)]
    CborMap<CborBytes, CborUlong> Amount
) : CborBase;