using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborList]
public readonly partial record struct VKeyWitness : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> VKey { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Signature { get; }
}
