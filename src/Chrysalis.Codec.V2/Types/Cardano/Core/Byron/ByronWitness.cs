using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxWitness : ICborType
{
    [CborOrder(0)] public partial int Variant { get; }
    [CborOrder(1)] public partial CborEncodedValue Data { get; }
}
