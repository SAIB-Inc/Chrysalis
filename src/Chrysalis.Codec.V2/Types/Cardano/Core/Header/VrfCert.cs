using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public readonly partial record struct VrfCert : ICborType
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> Proof { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Output { get; }
}
