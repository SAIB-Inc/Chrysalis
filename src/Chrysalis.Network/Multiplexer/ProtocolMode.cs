namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Specifies the mode of operation for the Ouroboros multiplexer.
/// </summary>
public enum ProtocolMode
{
    /// <summary>The initiator role, used by the client connecting to a Cardano node.</summary>
    Initiator,
    /// <summary>The responder role, used by the server receiving a connection.</summary>
    Responder
}
