using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record VotingProcedure(
    [CborIndex(0)] int Vote,
    [CborIndex(1)][CborNullable] Anchor? Anchor
) : CborBase<VotingProcedure>;