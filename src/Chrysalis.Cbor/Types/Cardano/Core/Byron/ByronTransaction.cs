using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Represents a Byron transaction payload containing the transaction and its witnesses.
/// </summary>
/// <param name="Transaction">The Byron transaction.</param>
/// <param name="Witnesses">The list of transaction witnesses.</param>
[CborSerializable]
[CborList]
public partial record ByronTxPayload(
    [CborOrder(0)] ByronTx Transaction,
    [CborOrder(1)] CborMaybeIndefList<ByronTxWitness> Witnesses
) : CborBase, ICborPreserveRaw;

/// <summary>
/// Represents a Byron-era transaction with inputs, outputs, and attributes.
/// </summary>
/// <param name="Inputs">The list of transaction inputs.</param>
/// <param name="Outputs">The list of transaction outputs.</param>
/// <param name="Attributes">Encoded transaction attributes.</param>
[CborSerializable]
[CborList]
public partial record ByronTx(
    [CborOrder(0)] CborMaybeIndefList<ByronTxIn> Inputs,
    [CborOrder(1)] CborMaybeIndefList<ByronTxOut> Outputs,
    [CborOrder(2)] CborEncodedValue Attributes
) : CborBase, ICborPreserveRaw;

/// <summary>
/// Represents a Byron transaction output with an address and ADA amount.
/// </summary>
/// <param name="Address">The destination Byron address.</param>
/// <param name="Amount">The output amount in lovelace.</param>
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
/// <param name="Variant">The input variant type (0 = standard spending).</param>
/// <param name="Data">The encoded input data containing transaction ID and index.</param>
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
/// <param name="Payload">The tag-24 encoded address payload.</param>
/// <param name="Crc">The CRC32 checksum of the address payload.</param>
[CborSerializable]
[CborList]
public partial record ByronAddress(
    [CborOrder(0)] CborEncodedValue Payload,
    [CborOrder(1)] uint Crc
) : CborBase;
