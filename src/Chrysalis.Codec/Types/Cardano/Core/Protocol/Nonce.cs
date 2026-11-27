using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborUnion(typeof(NonceWithHash), typeof(NonceWithoutHash))]
[Union]
public readonly partial struct Nonce : ICborType;

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct NonceWithHash : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Hash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct NonceWithoutHash : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
}
