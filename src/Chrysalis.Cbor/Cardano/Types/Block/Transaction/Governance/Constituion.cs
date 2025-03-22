using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record Constitution(
    [CborOrder(0)] Anchor Anchor,
    [CborOrder(1)][CborNullable] byte[]? ScriptHash
) : CborBase<Constitution>;