using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public readonly partial record struct ExUnitPrices : ICborType
{
    [CborOrder(0)] public partial CborRationalNumber MemPrice { get; }
    [CborOrder(1)] public partial CborRationalNumber StepPrice { get; }
}
