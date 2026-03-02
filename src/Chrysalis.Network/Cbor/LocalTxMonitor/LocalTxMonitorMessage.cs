using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Cbor.LocalTxMonitor;

/// <summary>
/// Base CBOR union type for all messages in the Ouroboros LocalTxMonitor mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record LocalTxMonitorMessage : CborBase;

/// <summary>
/// Factory methods for creating LocalTxMonitor mini-protocol request messages.
/// </summary>
public static class LocalTxMonitorMessages
{
    /// <summary>
    /// Creates an Acquire message to snapshot the current mempool state.
    /// </summary>
    /// <returns>An <see cref="Acquire"/> message.</returns>
    public static Acquire Acquire()
    {
        return new(1);
    }

    /// <summary>
    /// Creates a Release message to release the current mempool snapshot.
    /// </summary>
    /// <returns>A <see cref="Release"/> message.</returns>
    public static Release Release()
    {
        return new(3);
    }

    /// <summary>
    /// Creates a NextTx message to request the next transaction from the mempool snapshot.
    /// </summary>
    /// <returns>A <see cref="NextTx"/> message.</returns>
    public static NextTx NextTx()
    {
        return new(5);
    }

    /// <summary>
    /// Creates a HasTx message to check whether a transaction exists in the mempool.
    /// </summary>
    /// <param name="txId">The transaction ID to look up.</param>
    /// <returns>A <see cref="HasTx"/> message.</returns>
    public static HasTx HasTx(string txId)
    {
        return new(7, txId);
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
/// <param name="TxId">The transaction ID to check for in the mempool.</param>
[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record HasTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] string TxId
) : LocalTxMonitorMessage;

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
/// <param name="Sizes">A list of mempool size values (transaction count, total size in bytes, and capacity in bytes).</param>
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
/// <param name="Slot">The slot number at the time of measurement.</param>
/// <param name="Measures">A dictionary of named measurement categories, each containing a list of integer values.</param>
[CborSerializable]
[CborList]
[CborIndex(12)]
public partial record ReplyGetMeasures(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slot,
    [CborOrder(2)] Dictionary<string, CborDefList<int>> Measures
) : LocalTxMonitorMessage;

/// <summary>
/// Represents mempool measurement values including current size and maximum capacity.
/// </summary>
/// <param name="CurrentSize">The current size of the mempool in bytes.</param>
/// <param name="MaxCapacity">The maximum capacity of the mempool in bytes.</param>
[CborSerializable]
[CborList]
public partial record Measures(
    [CborOrder(0)] int CurrentSize,
    [CborOrder(1)] int MaxCapacity
) : CborBase;
