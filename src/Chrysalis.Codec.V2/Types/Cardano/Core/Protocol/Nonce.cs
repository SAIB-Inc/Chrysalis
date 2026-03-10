using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborUnion]
public partial interface INonce : ICborType;

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct NonceWithHash : INonce
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Hash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct NonceWithoutHash : INonce
{
    [CborOrder(0)] public partial int Tag { get; }
}
