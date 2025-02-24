using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake(AgentChannel channel) : IMiniProtocol
{
    public Aff<HandshakeMessage> Send(ProposeVersions proposeVersions) =>
        from proposeVersionsChunk in channel.EnqueueChunk(CborSerializer.Serialize(proposeVersions))
        from handshakeResponseChunk in channel.ReceiveNextChunk()
        let handshakeResponseMessage = CborSerializer.Deserialize<HandshakeMessage>(handshakeResponseChunk)
        select handshakeResponseMessage;
}