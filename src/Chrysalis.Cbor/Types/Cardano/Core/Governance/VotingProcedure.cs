using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public partial record VotingProcedure(
    [CborOrder(0)] int Vote,
    [CborOrder(1)][CborNullable] Anchor? Anchor
) : CborBase;