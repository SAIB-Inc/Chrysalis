using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Defines the interface for Ouroboros mini-protocols.
/// </summary>
public interface IMiniProtocol
{
    /// <summary>
    /// Gets the underlying communication channel for the protocol.
    /// </summary>
    AgentChannel Channel { get; }

    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    ProtocolType ProtocolType { get; }
}