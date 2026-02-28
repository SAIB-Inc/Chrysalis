using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Byron main block: [header, body, extra_attributes].
/// Structurally different from Shelley+ blocks.
/// </summary>
/// <param name="Header">The Byron block header.</param>
/// <param name="Body">The Byron block body containing transactions and payloads.</param>
/// <param name="Extra">Extra attributes encoded as CBOR values.</param>
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
/// <param name="Header">The Byron EBB header.</param>
/// <param name="Body">The EBB body as a list of byte arrays.</param>
/// <param name="Extra">Extra attributes encoded as CBOR values.</param>
[CborSerializable]
[CborList]
public partial record ByronEbBlock(
    [CborOrder(0)] ByronEbbHead Header,
    [CborOrder(1)] CborMaybeIndefList<ReadOnlyMemory<byte>> Body,
    [CborOrder(2)] CborMaybeIndefList<CborEncodedValue> Extra
) : Block, ICborPreserveRaw;
