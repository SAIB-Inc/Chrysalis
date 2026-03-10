using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborList]
public readonly partial record struct BootstrapWitness : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> PublicKey { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Signature { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> ChainCode { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> Attributes { get; }
}
