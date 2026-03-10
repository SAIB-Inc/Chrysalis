using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface IMultiAsset : ICborType;

[CborSerializable]
public readonly partial record struct MultiAssetOutput : IMultiAsset
{
    public partial Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> Value { get; }
}

[CborSerializable]
public readonly partial record struct MultiAssetMint : IMultiAsset
{
    public partial Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> Value { get; }
}
