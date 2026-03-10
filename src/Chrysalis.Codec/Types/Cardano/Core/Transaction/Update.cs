using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public readonly partial record struct Update : ICborType
{
    [CborOrder(0)] public partial ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates { get; }
    [CborOrder(1)] public partial ulong Epoch { get; }
}
