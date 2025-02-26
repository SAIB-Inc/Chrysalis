using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record CurrentEraQueryResponse(
    [CborIndex(0)] CborUlong CurrentEra
) : CborBase;