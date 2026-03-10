using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

namespace Chrysalis.Codec.V2.Types.Cardano.Core;

[CborSerializable]
[CborList]
public readonly partial record struct BlockWithEra : ICborType
{
    [CborOrder(0)] public partial int EraNumber { get; }
    [CborOrder(1)]
    [CborUnionHint(nameof(EraNumber), 0, typeof(ByronEbBlock))]
    [CborUnionHint(nameof(EraNumber), 1, typeof(ByronMainBlock))]
    [CborUnionHint(nameof(EraNumber), 2, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 3, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 4, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 5, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 6, typeof(BabbageBlock))]
    [CborUnionHint(nameof(EraNumber), 7, typeof(ConwayBlock))]
    public partial IBlock Block { get; }
}
