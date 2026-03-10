using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct ProposalProcedure : ICborType
{
    [CborOrder(0)] public partial ulong Deposit { get; }
    [CborOrder(1)] public partial RewardAccount RewardAccount { get; }
    [CborOrder(2)] public partial IGovAction GovAction { get; }
    [CborOrder(3)] public partial Anchor Anchor { get; }
}
