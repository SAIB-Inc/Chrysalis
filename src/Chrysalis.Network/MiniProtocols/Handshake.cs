using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);
    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync<HandshakeMessage>(propose, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken);
    }
}

