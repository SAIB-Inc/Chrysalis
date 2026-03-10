using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborList]
public readonly partial record struct PoolMetadata : ICborType
{
    [CborOrder(0)] public partial string Url { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> MetadataHash { get; }
}
