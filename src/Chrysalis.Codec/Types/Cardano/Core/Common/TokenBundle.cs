using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface ITokenBundle : ICborType;

[CborSerializable]
public readonly partial record struct TokenBundleOutput : ITokenBundle
{
    public partial CborMap<ReadOnlyMemory<byte>, ulong> Value { get; }
}

[CborSerializable]
public readonly partial record struct TokenBundleMint : ITokenBundle
{
    public partial CborMap<ReadOnlyMemory<byte>, long> Value { get; }
}
