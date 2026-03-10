using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

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
    /// <param name="rejectReason">The CBOR-encoded reason for the transaction rejection.</param>
    /// <returns>A <see cref="RejectTx"/> message.</returns>
    public static RejectTx RejectTx(CborEncodedValue rejectReason) => new(2, rejectReason);

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
/// <param name="RejectReason">The CBOR-encoded reason for the transaction rejection.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record RejectTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue RejectReason
) : LocalTxSubmissionMessage;

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
