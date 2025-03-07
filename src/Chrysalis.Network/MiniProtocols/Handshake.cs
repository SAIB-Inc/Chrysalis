using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake(AgentChannel channel) : IMiniProtocol
{
    public readonly ChannelBuffer Channel = new(channel);
    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        await Channel.SendFullMessageAsync(propose, cancellationToken);
        return await Channel.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken);
    }
}

