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
    public ChannelBuffer Channel = new(channel);
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();

    public async Task<ChainSyncMessage> FindIntersectionAsync(IEnumerable<Point> points, CancellationToken cancellationToken)
    {
        Points message = new([.. points]);
        MessageFindIntersect messageCbor = ChainSyncMessages.FindIntersect(message);
        await Channel.SendFullMessageAsync(messageCbor, cancellationToken);
        return await Channel.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<MessageNextResponse> NextRequestAsync(CancellationToken cancellationToken)
    {
        await Channel.SendFullMessageAsync(_nextRequest, cancellationToken);
        return await Channel.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken);
    }
}

