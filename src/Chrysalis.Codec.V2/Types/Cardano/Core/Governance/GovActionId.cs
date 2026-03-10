using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct GovActionId : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> TransactionId { get; }
    [CborOrder(1)] public partial ulong GovActionIndex { get; }
}
