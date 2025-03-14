using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

// [CborSerializable]
[CborList]
public partial record Constitution(
    [CborIndex(0)] Anchor Anchor,
    [CborIndex(1)][CborNullable] byte[]? ScriptHash
) : CborBase<Constitution>;