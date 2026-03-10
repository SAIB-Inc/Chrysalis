using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface IDatumOption : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct DatumHashOption : IDatumOption
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> DatumHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct InlineDatumOption : IDatumOption
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial CborEncodedValue Data { get; }
}
