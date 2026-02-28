using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Byron main block: [header, body, extra_attributes].
/// Structurally different from Shelley+ blocks.
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronMainBlock(
    [CborOrder(0)] ByronBlockHead Header,
    [CborOrder(1)] ByronBlockBody Body,
    [CborOrder(2)] CborMaybeIndefList<CborEncodedValue> Extra
) : Block, ICborPreserveRaw;

/// <summary>
/// Byron epoch boundary block (EBB): [header, body, extra_attributes].
/// EBBs are generated at epoch boundaries with minimal content.
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronEbBlock(
    [CborOrder(0)] ByronEbbHead Header,
    [CborOrder(1)] CborMaybeIndefList<byte[]> Body,
    [CborOrder(2)] CborMaybeIndefList<CborEncodedValue> Extra
) : Block, ICborPreserveRaw;
