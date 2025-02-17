using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public record Constitution(
    [CborIndex(0)] Anchor Anchor,
    [CborIndex(1)] CborNullable<CborBytes> ScriptHash
) : CborBase;