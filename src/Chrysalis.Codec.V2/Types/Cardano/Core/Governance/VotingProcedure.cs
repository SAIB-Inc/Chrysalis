using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct VotingProcedure : ICborType
{
    [CborOrder(0)] public partial int Vote { get; }
    [CborOrder(1)] public partial Anchor? Anchor { get; }
}
