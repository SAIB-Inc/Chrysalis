using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockBody : ICborType
{
    [CborOrder(0)] public partial ICborMaybeIndefList<ByronTxPayload> TxPayload { get; }
    [CborOrder(1)] public partial CborEncodedValue SscPayload { get; }
    [CborOrder(2)] public partial CborEncodedValue DlgPayload { get; }
    [CborOrder(3)] public partial CborEncodedValue UpdPayload { get; }
}
