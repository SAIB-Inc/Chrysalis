using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record VotingProcedure(
    [CborOrder(0)] int Vote,
    [CborOrder(1)][CborNullable] Anchor? Anchor
) : CborBase;