using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

/// <summary>
/// Base type for all Ouroboros ChainSync mini-protocol CBOR messages.
/// </summary>
[CborSerializable]
[CborUnion]
public partial record ChainSyncMessage : CborRecord;

/// <summary>
/// Intermediate union type for ChainSync responses to a next request (roll-forward, roll-backward, or await-reply).
/// </summary>
[CborSerializable]
[CborUnion]
public partial record MessageNextResponse : ChainSyncMessage;

/// <summary>
/// ChainSync client request for the next block or header from the server (MsgRequestNext).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 0 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record MessageNextRequest(
    [CborOrder(0)] int Idx
) : ChainSyncMessage;

/// <summary>
/// ChainSync server response indicating the client should wait for new data to become available (MsgAwaitReply).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record MessageAwaitReply(
   [CborOrder(0)] int Idx
) : MessageNextResponse;

/// <summary>
/// ChainSync server response carrying a new block or header and the current tip (MsgRollForward).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 2 for this message type).</param>
/// <param name="Payload">The CBOR-encoded block or header payload.</param>
/// <param name="Tip">The current tip of the chain as reported by the server.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record MessageRollForward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue Payload,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

/// <summary>
/// ChainSync server response indicating a rollback to a previous point on the chain (MsgRollBackward).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 3 for this message type).</param>
/// <param name="Point">The point on the chain to roll back to.</param>
/// <param name="Tip">The current tip of the chain as reported by the server.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record MessageRollBackward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;

/// <summary>
/// ChainSync client request to find an intersection between the client's chain and the server's chain (MsgFindIntersect).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 4 for this message type).</param>
/// <param name="Points">The list of known points on the client's chain to find an intersection with.</param>
[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record MessageFindIntersect(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Points Points
) : ChainSyncMessage;


/// <summary>
/// Intermediate union type for ChainSync intersection result messages.
/// </summary>
[CborSerializable]
[CborUnion]
public partial record MessageIntersectResult : ChainSyncMessage;

/// <summary>
/// ChainSync server response indicating an intersection was found at the given point (MsgIntersectFound).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 5 for this message type).</param>
/// <param name="Point">The point at which the intersection was found.</param>
/// <param name="Tip">The current tip of the chain as reported by the server.</param>
[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record MessageIntersectFound(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

/// <summary>
/// ChainSync server response indicating no intersection was found with the provided points (MsgIntersectNotFound).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 6 for this message type).</param>
/// <param name="Tip">The current tip of the chain as reported by the server.</param>
[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record MessageIntersectNotFound(
    [CborOrder(0)] int Idx,
    [CborOrder(2)] Tip Tip
) : MessageIntersectResult;

/// <summary>
/// ChainSync message indicating the protocol session is done (MsgDone).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 7 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record MessageDone(
    [CborOrder(0)] int Idx
) : ChainSyncMessage;


/// <summary>
/// Factory methods for constructing Ouroboros ChainSync mini-protocol messages with correct CBOR indices.
/// </summary>
public static class ChainSyncMessages
{
    /// <summary>
    /// Creates a <see cref="MessageNextRequest"/> to request the next block or header from the ChainSync server.
    /// </summary>
    /// <returns>A new <see cref="MessageNextRequest"/> with the correct CBOR index.</returns>
    public static MessageNextRequest NextRequest()
    {
        return new(0);
    }

    /// <summary>
    /// Creates a <see cref="MessageFindIntersect"/> to find a common intersection point between client and server chains.
    /// </summary>
    /// <param name="points">The list of known points on the client's chain.</param>
    /// <returns>A new <see cref="MessageFindIntersect"/> with the correct CBOR index.</returns>
    public static MessageFindIntersect FindIntersect(Points points)
    {
        return new(4, points);
    }

    /// <summary>
    /// Creates a <see cref="MessageDone"/> to signal the end of the ChainSync session.
    /// </summary>
    /// <returns>A new <see cref="MessageDone"/> with the correct CBOR index.</returns>
    public static MessageDone Done()
    {
        return new(7);
    }
}
