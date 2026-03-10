using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.KeepAlive;

/// <summary>
/// Base type for all Ouroboros KeepAlive mini-protocol CBOR messages.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record KeepAliveMessage : CborRecord;

/// <summary>
/// KeepAlive ping message sent to verify the remote peer is still responsive (MsgKeepAlive).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 0 for this message type).</param>
/// <param name="Cookie">An opaque cookie value that the peer must echo back in its response.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record MessageKeepAlive(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

/// <summary>
/// KeepAlive response echoing back the cookie from the corresponding ping (MsgKeepAliveResponse).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 1 for this message type).</param>
/// <param name="Cookie">The cookie value echoed back from the original KeepAlive request.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record MessageKeepAliveResponse(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

/// <summary>
/// KeepAlive message indicating the protocol session is done (MsgDone).
/// </summary>
/// <param name="Idx">The CBOR message index identifier (always 2 for this message type).</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record MessageDone(
    [CborOrder(0)] int Idx
) : KeepAliveMessage;

/// <summary>
/// Factory methods for constructing Ouroboros KeepAlive mini-protocol messages with correct CBOR indices.
/// </summary>
public static class KeepAliveMessages
{
    /// <summary>
    /// Creates a <see cref="MessageKeepAlive"/> ping to verify the remote peer is responsive.
    /// </summary>
    /// <param name="cookie">An opaque cookie value the peer must echo back.</param>
    /// <returns>A new <see cref="MessageKeepAlive"/> with the correct CBOR index.</returns>
    public static MessageKeepAlive KeepAlive(uint cookie)
    {
        return new(0, cookie);
    }

    /// <summary>
    /// Creates a <see cref="MessageKeepAliveResponse"/> echoing back the received cookie.
    /// </summary>
    /// <param name="cookie">The cookie value from the KeepAlive request to echo back.</param>
    /// <returns>A new <see cref="MessageKeepAliveResponse"/> with the correct CBOR index.</returns>
    public static MessageKeepAliveResponse KeepAliveResponse(uint cookie)
    {
        return new(1, cookie);
    }

    /// <summary>
    /// Creates a <see cref="MessageDone"/> to signal the end of the KeepAlive session.
    /// </summary>
    /// <returns>A new <see cref="MessageDone"/> with the correct CBOR index.</returns>
    public static MessageDone Done()
    {
        return new(2);
    }
}
