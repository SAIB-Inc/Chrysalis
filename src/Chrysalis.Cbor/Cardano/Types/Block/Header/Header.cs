using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record BlockHeader(
    [CborIndex(0)] BlockHeaderBody HeaderBody,
    [CborIndex(1)] CborBytes BodySignature
) : CborBase;
