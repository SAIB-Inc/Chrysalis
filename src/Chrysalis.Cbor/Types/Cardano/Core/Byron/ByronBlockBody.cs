using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Represents the body of a Byron block containing transactions, SSC, delegation, and update payloads.
/// </summary>
/// <param name="TxPayload">The list of transaction payloads.</param>
/// <param name="SscPayload">The shared seed computation payload.</param>
/// <param name="DlgPayload">The list of delegation certificates.</param>
/// <param name="UpdPayload">The update proposal payload.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockBody(
    [CborOrder(0)] CborMaybeIndefList<ByronTxPayload> TxPayload,
    [CborOrder(1)] CborEncodedValue SscPayload,
    [CborOrder(2)] CborMaybeIndefList<ByronDlg> DlgPayload,
    [CborOrder(3)] CborEncodedValue UpdPayload
) : CborBase;
