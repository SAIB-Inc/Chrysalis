using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public readonly partial record struct ProtocolVersion : ICborType
{
    [CborOrder(0)] public partial ulong Major { get; }
    [CborOrder(1)] public partial ulong SequenceNumber { get; }
}
