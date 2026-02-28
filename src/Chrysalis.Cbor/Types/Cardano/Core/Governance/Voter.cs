using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Identifies a voter in the Cardano governance system by tag type and credential hash.
/// </summary>
/// <param name="Tag">The voter type tag (0 = ConstitutionalCommittee, 1 = DRep, 2 = StakePool).</param>
/// <param name="Hash">The credential hash identifying the voter.</param>
[CborSerializable]
[CborList]
public partial record Voter(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> Hash
) : CborBase;
