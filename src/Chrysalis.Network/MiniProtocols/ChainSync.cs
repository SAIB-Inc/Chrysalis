using System.Drawing;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class ChainSync(AgentChannel channel) : IMiniProtocol
{
    public Aff<ChainSyncMessage> FindInterection(IEnumerable<Cbor.Common.Point> points) =>
        from pointsCboralized in Aff(() => ValueTask.FromResult(new Points([.. points])))
        from findIntersectMessage in Aff(() => ValueTask.FromResult(ChainSyncMessages.FindIntersect(pointsCboralized)))
        from findIntersectChunk in channel.EnqueueChunk(CborSerializer.Serialize(findIntersectMessage))
        from intersectionChunk in channel.ReceiveNextChunk()
        let intersectionMessage = CborSerializer.Deserialize<ChainSyncMessage>(intersectionChunk)
        select intersectionMessage;
}

