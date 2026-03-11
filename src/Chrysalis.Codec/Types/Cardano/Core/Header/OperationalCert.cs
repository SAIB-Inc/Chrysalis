using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public readonly partial record struct OperationalCert : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> HotVkey { get; }
    [CborOrder(1)] public partial ulong SequenceNumber { get; }
    [CborOrder(2)] public partial ulong KesPeriod { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> Sigma { get; }
}
