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

    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(propose, cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken);
    }
}

