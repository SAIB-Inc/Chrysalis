using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public readonly partial record struct Anchor : ICborType
{
    [CborOrder(0)] public partial string Url { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ContentHash { get; }
}
