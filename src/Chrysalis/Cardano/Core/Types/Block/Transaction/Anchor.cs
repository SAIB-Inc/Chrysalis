using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction;

[CborConverter(typeof(CustomListConverter))]
public record Anchor(
    [CborProperty(0)] CborText AnchorUrl,
    [CborProperty(1)] CborBytes AnchorDataHash
) : CborBase;