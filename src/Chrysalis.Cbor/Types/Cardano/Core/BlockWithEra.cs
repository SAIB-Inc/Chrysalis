using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core;

/// <summary>
/// A Cardano block tagged with its era number, used for multi-era block decoding.
/// </summary>
/// <param name="EraNumber">The era identifier (e.g., 1 = Byron, 2 = Shelley, etc.).</param>
/// <param name="Block">The block data.</param>
[CborSerializable]
[CborList]
public partial record BlockWithEra(
    [CborOrder(0)] int EraNumber,
    [CborOrder(1)] Block Block
) : CborBase, ICborPreserveRaw;
