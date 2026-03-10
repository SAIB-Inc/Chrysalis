using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public readonly partial record struct TransactionInput : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> TransactionId { get; }
    [CborOrder(1)] public partial ulong Index { get; }
}
