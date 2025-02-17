using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborConverter(typeof(CustomListConverter))]
public record Anchor(
    [CborIndex(0)] CborText AnchorUrl,
    [CborIndex(1)] CborBytes AnchorDataHash
) : CborBase;