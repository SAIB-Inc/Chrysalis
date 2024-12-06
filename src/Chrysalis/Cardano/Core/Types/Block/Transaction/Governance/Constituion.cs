using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public record Constitution(
    [CborProperty(0)] Anchor Anchor,
    [CborProperty(1)] CborNullable<CborBytes> ScriptHash
) : CborBase;