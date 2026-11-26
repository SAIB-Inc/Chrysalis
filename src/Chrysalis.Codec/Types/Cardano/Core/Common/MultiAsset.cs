using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface IMultiAsset : ICborType;

[CborSerializable]
public readonly partial record struct MultiAssetOutput : IMultiAsset
{
    public partial CborMap<ReadOnlyMemory<byte>, TokenBundleOutput> Value { get; }
}

[CborSerializable]
public readonly partial record struct MultiAssetMint : IMultiAsset
{
    public partial CborMap<ReadOnlyMemory<byte>, TokenBundleMint> Value { get; }
}
