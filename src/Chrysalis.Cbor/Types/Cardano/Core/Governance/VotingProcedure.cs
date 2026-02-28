using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// A single voting procedure containing the vote and optional metadata anchor.
/// </summary>
/// <param name="Vote">The vote value (0 = No, 1 = Yes, 2 = Abstain).</param>
/// <param name="Anchor">The optional metadata anchor providing rationale for the vote.</param>
[CborSerializable]
[CborList]
public partial record VotingProcedure(
    [CborOrder(0)] int Vote,
    [CborOrder(1)][CborNullable] Anchor? Anchor
) : CborBase;
