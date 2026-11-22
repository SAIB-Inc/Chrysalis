using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.LocalTxSubmit;

/// <summary>
/// Base CBOR union type for all messages in the Ouroboros LocalTxSubmit mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record LocalTxSubmissionMessage : CborRecord;

/// <summary>
/// Factory methods for creating LocalTxSubmit mini-protocol messages.
/// </summary>
public static class LocalTxSubmissionMessages
{
    /// <summary>
    /// Creates a SubmitTx message to submit an era-tagged transaction to the Cardano node.
    /// </summary>
    /// <param name="eraTx">The era-tagged transaction to submit.</param>
    /// <returns>A <see cref="SubmitTx"/> message.</returns>
    public static SubmitTx SubmitTx(EraTx eraTx) => new(0, eraTx);

    /// <summary>
    /// Creates an AcceptTx message indicating the submitted transaction was accepted.
    /// </summary>
    /// <returns>An <see cref="AcceptTx"/> message.</returns>
    public static AcceptTx AcceptTx() => new(1);

    /// <summary>
    /// Creates a RejectTx message indicating the submitted transaction was rejected.
    /// </summary>
    /// <param name="rejectReason">The node's reason, the CBOR item exactly as the node sends it.</param>
    /// <returns>A <see cref="RejectTx"/> message.</returns>
    public static RejectTx RejectTx(CborAny rejectReason) => new(2, rejectReason);

    /// <summary>
    /// Creates a Done message to terminate the LocalTxSubmit protocol session.
    /// </summary>
    /// <returns>A <see cref="Done"/> message.</returns>
    public static Done Done() => new(3);
}

/// <summary>
/// Represents the SubmitTx message in the Ouroboros LocalTxSubmit mini-protocol, carrying a transaction to submit.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="EraTx">The era-tagged transaction to submit to the Cardano node.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record SubmitTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] EraTx EraTx
) : LocalTxSubmissionMessage;

/// <summary>
/// Represents the AcceptTx message in the Ouroboros LocalTxSubmit mini-protocol, indicating the submitted transaction was accepted into the mempool.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record AcceptTx(
    [CborOrder(0)] int Idx
) : LocalTxSubmissionMessage;

/// <summary>
/// Represents the RejectTx message in the Ouroboros LocalTxSubmit mini-protocol, indicating the submitted transaction was rejected.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="RejectReason">The node's reason: the inline CBOR item exactly as received. <see cref="Describe"/> reads it as text.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record RejectTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborAny RejectReason
) : LocalTxSubmissionMessage
{
    /// <summary>Describes the node's reason as readable text; never throws.</summary>
    /// <returns>
    /// For an era mismatch, the transaction's era and the ledger's; for a ledger rejection, its era index and the
    /// era's errors in CBOR diagnostic notation (RFC 8949 §8; text strings are JSON-escaped via
    /// <see cref="System.Text.Json.JsonEncodedText"/> with
    /// <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>, escaping <c>"</c>,
    /// <c>\</c>, control characters, and any other code points that encoder forbids); otherwise the whole reason in
    /// that notation. A reason holding a float, another simple value, a negative below -2^63 or indefinite-length
    /// text, nested deeper than 64, or not well-formed, is given whole as hex, h'…'.
    /// </returns>
    public string Describe()
    {
        ReadOnlySpan<byte> raw = RejectReason.Raw.Span;
        try
        {
            // Checks the whole reason is one well-formed item within MaxDepth before dispatching.
            CborReader check = new(raw);
            check.SkipDataItem(CborText.MaxDepth);
            if (check.DataAvailable) { throw check.BuildException("Trailing bytes after CBOR item"); }
            // [[era, errors]] for a ledger rejection, [txEra, ledgerEra] for an era mismatch
            // (ouroboros-consensus 5.4.0.0, HardFork/Combinator/Serialisation/Common.hs:534-557).
            return raw switch
            {
                [0x82, ..] => CborText.EraMismatch(raw, "transaction"),
                [0x81, 0x82, <= 0x17, ..] => $"Rejected in era {raw[2]}: {CborText.Diagnostic(raw[3..])}",
                _ => CborText.Diagnostic(raw),
            };
        }
        catch (CborException)
        {
            return $"h'{Convert.ToHexStringLower(raw)}'";
        }
    }
}

/// <summary>
/// Represents the Done message in the Ouroboros LocalTxSubmit mini-protocol, signaling that the client is terminating the protocol session.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Done(
    [CborOrder(0)] int Idx
) : LocalTxSubmissionMessage;

/// <summary>
/// Represents an era-tagged transaction used in Ouroboros mini-protocol messages.
/// </summary>
/// <param name="Era">The numeric identifier of the Cardano era this transaction belongs to.</param>
/// <param name="Tx">The CBOR-encoded transaction bytes.</param>
[CborSerializable]
[CborList]
public partial record EraTx(
    [CborOrder(0)] int Era,
    [CborOrder(1)] CborEncodedValue Tx
) : CborRecord;
