using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents a message consisting of a protocol type and payload.
/// </summary>
/// <param name="Protocol">The protocol type.</param>
/// <param name="Payload">The payload data.</param>
public record ProtocolMessage(ProtocolType Protocol, byte[] Payload);
