using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.BlockFetch;

/// <summary>
/// Base type for all Ouroboros BlockFetch mini-protocol CBOR messages.
/// </summary>
[CborSerializable]
[CborUnion]
public partial record BlockFetchMessage : CborRecord;

/// <summary>
/// BlockFetch client request to fetch a range of blocks between two points (MsgRequestRange).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 0 for this message type).</param>
/// <param name="From">The starting point of the block range to fetch.</param>
/// <param name="To">The ending point of the block range to fetch.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record RequestRange(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point From,
    [CborOrder(2)] Point To
) : BlockFetchMessage;

/// <summary>
/// BlockFetch client message indicating it is done requesting blocks (MsgClientDone).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record ClientDone(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

/// <summary>
/// BlockFetch server response indicating the start of a batch of blocks (MsgStartBatch).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 2 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record StartBatch(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

/// <summary>
/// BlockFetch server response indicating no blocks are available for the requested range (MsgNoBlocks).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 3 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record NoBlocks(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

/// <summary>
/// BlockFetch server message carrying a single CBOR-encoded block body (MsgBlock).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 4 for this message type).</param>
/// <param name="Body">The CBOR-encoded block body payload.</param>
[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record BlockBody(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue Body
) : BlockFetchMessage;

/// <summary>
/// BlockFetch server message indicating the end of a batch of blocks (MsgBatchDone).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 5 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record BatchDone(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

/// <summary>
/// Factory methods for constructing Ouroboros BlockFetch mini-protocol messages with correct CBOR indices.
/// </summary>
public static class BlockFetchMessages
{
    /// <summary>
    /// Creates a <see cref="RequestRange"/> to fetch blocks between two chain points.
    /// </summary>
    /// <param name="from">The starting point of the block range.</param>
    /// <param name="to">The ending point of the block range.</param>
    /// <returns>A new <see cref="RequestRange"/> with the correct CBOR index.</returns>
    public static RequestRange RequestRange(Point from, Point to)
    {
        return new(0, from, to);
    }

    /// <summary>
    /// Creates a <see cref="ClientDone"/> to signal the end of the BlockFetch session.
    /// </summary>
    /// <returns>A new <see cref="ClientDone"/> with the correct CBOR index.</returns>
    public static ClientDone ClientDone()
    {
        return new(1);
    }
}
