using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Point = Chrysalis.Network.Cbor.Common.Point;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros ChainSync mini-protocol.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ChainSync protocol.
/// </remarks>
/// <param name="channel">The channel for protocol communication.</param>
public class ChainSync(AgentChannel channel) : IMiniProtocol
{
    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    public static ProtocolType ProtocolType => ProtocolType.ClientChainSync;

    private readonly ChannelBuffer _buffer = new(channel);
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();

    /// <summary>
    /// Finds an intersection point between the local and remote chains.
    /// </summary>
    /// <param name="points">Collection of points to check for intersection.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message indicating intersection status.</returns>
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

