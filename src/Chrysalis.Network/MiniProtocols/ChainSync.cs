using System.Drawing;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class ChainSync(AgentChannel channel) : IMiniProtocol
{
    //private readonly ChannelBuffer _buffer = new(channel);
    // public Aff<MessageIntersectResult> FindInterection(IEnumerable<Cbor.Common.Point> points) =>
    //     from pointsCboralized in Aff(() => ValueTask.FromResult(new Points([.. points])))
    //     from findIntersectMessage in Aff(() => ValueTask.FromResult(ChainSyncMessages.FindIntersect(pointsCboralized)))
    //     from findIntersectChunk in _buffer.SendFullMessage(findIntersectMessage)
    //     from intersectionMessage in _buffer.RecieveFullMessage<MessageIntersectResult>()
    //     select intersectionMessage;

    public Aff<byte[]> FindInterectionBenchmark(byte[] findIntersectMessage) =>
        from findIntersectChunk in channel.EnqueueChunk(findIntersectMessage)
        from intersectionMessage in channel.ReceiveNextChunk()
        select intersectionMessage;

    // public Aff<MessageNextResponse> NextRequest() =>
    //     from nextRequestMessage in Aff(() => ValueTask.FromResult(ChainSyncMessages.NextRequest()))
    //     from nextRequestChunk in _buffer.SendFullMessage(nextRequestMessage)
    //     from nextResponseMessage in _buffer.RecieveFullMessage<MessageNextResponse>()
    //     select nextResponseMessage;

    public Aff<byte[]> NextRequest(byte[] nextMessage) =>
        from nextRequestChunk in channel.EnqueueChunk(nextMessage)
        from nextResponseMessage in channel.ReceiveNextChunk()
        select nextResponseMessage;
}

