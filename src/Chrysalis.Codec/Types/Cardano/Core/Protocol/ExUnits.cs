using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public readonly partial record struct ExUnits : ICborType
{
    [CborOrder(0)] public partial ulong Mem { get; }
    [CborOrder(1)] public partial ulong Steps { get; }
}
