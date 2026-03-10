using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public readonly partial record struct ExUnits : ICborType
{
    [CborOrder(0)] public partial ulong Mem { get; }
    [CborOrder(1)] public partial ulong Steps { get; }
}
