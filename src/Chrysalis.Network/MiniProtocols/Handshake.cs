using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class Handshake<T>(AgentChannel channel) : IMiniProtocol
{
    public Aff<HandshakeMessage> Send() =>
        from _ in channel.EnqueueChunk(Convert.FromHexString("8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4"))
        from handshakeResponseChunk in channel.ReceiveNextChunk()
        let handshakeResponseMessage = CborSerializer.Deserialize<HandshakeMessage>(handshakeResponseChunk)
        select handshakeResponseMessage;
}