using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public readonly partial record struct Credential : ICborType
{
    [CborOrder(0)] public partial int Type { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Hash { get; }
}
