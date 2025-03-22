using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;
using Point = Chrysalis.Network.Cbor.Common.Point;

namespace Chrysalis.Network.MiniProtocols;

public class ChainSync(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();

    public async Task<ChainSyncMessage> FindIntersectionAsync(IEnumerable<Point> points, CancellationToken cancellationToken)
    {
        Points message = new([.. points]);
        MessageFindIntersect messageCbor = ChainSyncMessages.FindIntersect(message);
        await _channelBuffer.SendFullMessageAsync<ChainSyncMessage>(messageCbor, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<MessageNextResponse?> NextRequestAsync(CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(_nextRequest, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken) as MessageNextResponse;
    }
}

