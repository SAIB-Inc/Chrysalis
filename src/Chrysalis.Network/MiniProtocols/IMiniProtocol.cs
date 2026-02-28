namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Defines the interface for Ouroboros mini-protocols.
/// </summary>
public interface IMiniProtocol
{
    /// <summary>
    /// Gets a value indicating whether the protocol is done.
    /// </summary>
    bool IsDone { get; }
}
