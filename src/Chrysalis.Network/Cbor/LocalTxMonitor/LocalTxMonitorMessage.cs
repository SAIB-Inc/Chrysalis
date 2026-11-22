using System.Buffers;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Cbor.LocalTxMonitor;

/// <summary>
/// Base CBOR union type for all messages in the Ouroboros LocalTxMonitor mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record LocalTxMonitorMessage : CborRecord;

/// <summary>
/// Factory methods for creating LocalTxMonitor mini-protocol request messages.
/// </summary>
public static class LocalTxMonitorMessages
{
    /// <summary>
    /// Creates an Acquire message to snapshot the current mempool state.
    /// </summary>
    /// <returns>An <see cref="Acquire"/> message.</returns>
    public static Acquire Acquire() => new(1);

    /// <summary>
    /// Creates a Release message to release the current mempool snapshot.
    /// </summary>
    /// <returns>A <see cref="Release"/> message.</returns>
    public static Release Release() => new(3);

    /// <summary>
    /// Creates a NextTx message to request the next transaction from the mempool snapshot.
    /// </summary>
    /// <returns>A <see cref="NextTx"/> message.</returns>
    public static NextTx NextTx() => new(5);

    /// <summary>
    /// Creates a HasTx message for a transaction id given as 64 hexadecimal digits, sent with the Conway era.
    /// </summary>
    /// <param name="txId">The transaction id as 64 hexadecimal digits.</param>
    /// <returns>A <see cref="LocalTxMonitor.HasTx"/> message.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="txId"/> is not 64 hexadecimal digits.</exception>
    public static HasTx HasTx(string txId) => HasTx(ConwayTxId(txId));

    /// <summary>
    /// Creates a HasTx message to check whether a transaction is in the acquired mempool snapshot.
    /// </summary>
    /// <param name="txId">The era-tagged transaction id.</param>
    /// <returns>A <see cref="LocalTxMonitor.HasTx"/> message.</returns>
    /// <exception cref="ArgumentException">Thrown when the id is not 32 bytes.</exception>
    public static HasTx HasTx(EraTxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return txId.Hash.Length == 32 ? new(7, txId) : throw new ArgumentException("Expected a 32-byte transaction id.", nameof(txId));
    }

    // A hexadecimal id is sent with the Conway era: the node finds a transaction by its id whatever the era
    // (ouroboros-consensus 5.4.0.0, HardFork/Combinator/AcrossEras.hs:449-463, Mempool/Impl/Common.hs:672).
    internal static EraTxId ConwayTxId(string txId)
    {
        byte[] hash = new byte[32];
        return Convert.FromHexString(txId, hash, out _, out int written) == OperationStatus.Done && written == hash.Length
            ? new EraTxId((int)Era.Conway, hash)
            : throw new ArgumentException("Expected a transaction id of 64 hexadecimal digits.", nameof(txId));
    }

    /// <summary>
    /// Gets a default GetSizes message to query the mempool capacity and sizes.
    /// </summary>
    public static GetSizes DefaultGetSizes => new(9);

    /// <summary>
    /// Gets a default GetMeasures message to query mempool measurement data.
    /// </summary>
    public static GetMeasures DefaultGetMeasures => new(11);
}

/// <summary>
/// Represents the Done message in the LocalTxMonitor mini-protocol, indicating the protocol session has ended.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record Done(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the Acquire message in the LocalTxMonitor mini-protocol, requesting a snapshot of the current mempool.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record Acquire(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the Acquired message in the LocalTxMonitor mini-protocol, confirming that a mempool snapshot has been acquired.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Slot">The slot number at which the mempool snapshot was taken.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Acquired(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slot
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the Release message in the LocalTxMonitor mini-protocol, releasing the current mempool snapshot.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Release(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the NextTx message in the LocalTxMonitor mini-protocol, requesting the next transaction from the mempool snapshot.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record NextTx(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the ReplyNextTx message in the LocalTxMonitor mini-protocol, containing the next transaction from the mempool or null if exhausted.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="EraTx">The era-tagged transaction from the mempool, or null if no more transactions are available.</param>
[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record ReplyNextTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)][CborNullable] EraTx? EraTx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the HasTx message in the LocalTxMonitor mini-protocol, querying whether a specific transaction is in the mempool.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="TxId">The era-tagged id of the transaction to look for.</param>
[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record HasTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] EraTxId TxId
) : LocalTxMonitorMessage;

/// <summary>
/// An era-tagged transaction id, <c>[era, id]</c>, as the node reads it in a has-tx query.
/// </summary>
/// <param name="Era">The era index of the transaction, such as 6 for Conway.</param>
/// <param name="Hash">The 32-byte transaction id: the Blake2b-256 hash of the transaction body.</param>
[CborSerializable]
[CborList]
public partial record EraTxId(
    [CborOrder(0)] int Era,
    [CborOrder(1)] ReadOnlyMemory<byte> Hash
) : CborRecord;

/// <summary>
/// Represents the ReplyHasTx message in the LocalTxMonitor mini-protocol, indicating whether the queried transaction exists in the mempool.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="HasTx">True if the transaction exists in the mempool; otherwise false.</param>
[CborSerializable]
[CborList]
[CborIndex(8)]
public partial record ReplyHasTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] bool HasTx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the GetSizes message in the LocalTxMonitor mini-protocol, requesting the mempool capacity and size information.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(9)]
public partial record GetSizes(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the ReplyGetSizes message in the LocalTxMonitor mini-protocol, containing the mempool size information.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Sizes">The mempool's capacity in bytes, the total size of its transactions in bytes and the number of transactions, in that order.</param>
[CborSerializable]
[CborList]
[CborIndex(10)]
public partial record ReplyGetSizes(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] List<ulong> Sizes
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the GetMeasures message in the LocalTxMonitor mini-protocol, requesting mempool measurement data.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(11)]
public partial record GetMeasures(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

/// <summary>
/// Represents the ReplyGetMeasures message in the LocalTxMonitor mini-protocol, containing mempool measurement data.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Slot">The number of transactions in the snapshot (the protocol's txCount), not a slot.</param>
/// <param name="Measures">Each measure by name, such as ex_units_steps, with its size and capacity as 64-bit values.</param>
[CborSerializable]
[CborList]
[CborIndex(12)]
public partial record ReplyGetMeasures(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slot,
    [CborOrder(2)] Dictionary<string, CborList<ulong>> Measures
) : LocalTxMonitorMessage;

/// <summary>
/// Represents one mempool measure: its current size and its capacity.
/// </summary>
/// <param name="CurrentSize">The measure's current size.</param>
/// <param name="MaxCapacity">The measure's capacity.</param>
[CborSerializable]
[CborList]
public partial record Measures(
    [CborOrder(0)] ulong CurrentSize,
    [CborOrder(1)] ulong MaxCapacity
) : CborRecord;
