using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxWitness : ICborType
{
    [CborOrder(0)] public partial int Variant { get; }
    [CborOrder(1)] public partial CborEncodedValue Data { get; }
}
