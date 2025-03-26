using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public partial record Constitution(
    [CborOrder(0)] Anchor Anchor,
    [CborOrder(1)][CborNullable] byte[]? ScriptHash
) : CborBase;