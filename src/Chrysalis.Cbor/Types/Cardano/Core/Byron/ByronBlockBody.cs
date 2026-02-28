using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public partial record ByronBlockBody(
    [CborOrder(0)] CborMaybeIndefList<ByronTxPayload> TxPayload,
    [CborOrder(1)] CborEncodedValue SscPayload,
    [CborOrder(2)] CborMaybeIndefList<ByronDlg> DlgPayload,
    [CborOrder(3)] CborEncodedValue UpdPayload
) : CborBase;
