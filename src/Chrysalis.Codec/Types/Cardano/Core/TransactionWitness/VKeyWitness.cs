using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborList]
public readonly partial record struct VKeyWitness : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> VKey { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Signature { get; }
}
