using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake(AgentChannel channel) : IMiniProtocol
{
    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    public static ProtocolType ProtocolType => ProtocolType.Handshake;

    private readonly ChannelBuffer _buffer = new(channel);

    /// <summary>
    /// Sends a version proposal and receives the handshake response.
    /// </summary>
    /// <param name="propose">The version proposal message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The handshake response message.</returns>
    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync<HandshakeMessage>(propose, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken);
    }
}

