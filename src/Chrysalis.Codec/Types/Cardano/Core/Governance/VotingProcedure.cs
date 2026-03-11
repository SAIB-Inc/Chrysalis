using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct VotingProcedure : ICborType
{
    [CborOrder(0)] public partial int Vote { get; }
    [CborOrder(1)] public partial Anchor? Anchor { get; }
}
