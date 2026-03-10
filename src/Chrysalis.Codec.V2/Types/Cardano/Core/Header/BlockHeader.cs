using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public readonly partial record struct BlockHeader : ICborType
{
    [CborOrder(0)] public partial IBlockHeaderBody HeaderBody { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> BodySignature { get; }
}
