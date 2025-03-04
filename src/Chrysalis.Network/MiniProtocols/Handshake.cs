using System.Buffers;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake(AgentChannel channel) : IMiniProtocol
{
    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        byte[] data = CborSerializer.Serialize(propose);
        await channel.EnqueueChunkAsync(new(data), cancellationToken);
        ReadOnlySequence<byte> responseData = await channel.ReadChunkAsync(cancellationToken);
        return CborSerializer.Deserialize<HandshakeMessage>(responseData.First);
    }
}

