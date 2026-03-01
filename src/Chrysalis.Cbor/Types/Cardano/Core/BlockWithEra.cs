using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;

namespace Chrysalis.Cbor.Types.Cardano.Core;

/// <summary>
/// A Cardano block tagged with its era number, used for multi-era block decoding.
/// </summary>
/// <param name="EraNumber">The era identifier (0 = Byron EBB, 1 = Byron, 2 = Shelley, etc.).</param>
/// <param name="Block">The block data, dispatched by EraNumber.</param>
[CborSerializable]
[CborList]
public partial record BlockWithEra(
    [CborOrder(0)] int EraNumber,
    [CborOrder(1)]
    [CborUnionHint(nameof(EraNumber), 0, typeof(ByronEbBlock))]
    [CborUnionHint(nameof(EraNumber), 1, typeof(ByronMainBlock))]
    [CborUnionHint(nameof(EraNumber), 2, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 3, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 4, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 5, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 6, typeof(BabbageBlock))]
    [CborUnionHint(nameof(EraNumber), 7, typeof(ConwayBlock))]
    Block Block
) : CborBase, ICborPreserveRaw;
