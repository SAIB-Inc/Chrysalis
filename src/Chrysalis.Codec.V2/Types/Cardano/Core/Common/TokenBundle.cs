using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface ITokenBundle : ICborType;

[CborSerializable]
public readonly partial record struct TokenBundleOutput : ITokenBundle
{
    public partial Dictionary<ReadOnlyMemory<byte>, ulong> Value { get; }
}

[CborSerializable]
public readonly partial record struct TokenBundleMint : ITokenBundle
{
    public partial Dictionary<ReadOnlyMemory<byte>, long> Value { get; }
}
