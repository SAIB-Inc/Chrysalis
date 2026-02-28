using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public partial record ByronTxPayload(
    [CborOrder(0)] ByronTx Transaction,
    [CborOrder(1)] CborMaybeIndefList<ByronTxWitness> Witnesses
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record ByronTx(
    [CborOrder(0)] CborMaybeIndefList<ByronTxIn> Inputs,
    [CborOrder(1)] CborMaybeIndefList<ByronTxOut> Outputs,
    [CborOrder(2)] CborEncodedValue Attributes
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record ByronTxOut(
    [CborOrder(0)] ByronAddress Address,
    [CborOrder(1)] ulong Amount
) : CborBase;

/// <summary>
/// Byron TxIn encoded as [variant, #6.24(cbor([txid, index]))].
/// Variant 0 is the standard spending input.
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronTxIn(
    [CborOrder(0)] int Variant,
    [CborOrder(1)] CborEncodedValue Data
) : CborBase;

/// <summary>
/// Byron address: [#6.24(payload_bytes), crc32].
/// The payload is a tag-24 wrapped CBOR byte string containing [address_id, attributes, type].
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronAddress(
    [CborOrder(0)] CborEncodedValue Payload,
    [CborOrder(1)] uint Crc
) : CborBase;
