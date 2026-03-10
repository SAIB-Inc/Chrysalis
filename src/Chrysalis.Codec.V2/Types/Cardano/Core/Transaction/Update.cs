using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public readonly partial record struct Update : ICborType
{
    [CborOrder(0)] public partial ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates { get; }
    [CborOrder(1)] public partial ulong Epoch { get; }
}
