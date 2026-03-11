using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public readonly partial record struct VrfCert : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> Proof { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Output { get; }
}
