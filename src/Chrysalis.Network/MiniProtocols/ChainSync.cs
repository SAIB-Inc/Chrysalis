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
    private readonly ChannelBuffer _buffer = new(channel);
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();

    /// <summary>
    /// Gets the communication channel used by this protocol.
    /// </summary>
    public AgentChannel Channel { get; } = channel;

    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    public ProtocolType ProtocolType => ProtocolType.ClientChainSync;

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
        await _buffer.SendFullMessageAsync(messageCbor, cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken);
    }

    /// <summary>
    /// Requests the next block from the chain.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message containing next block or chain status.</returns>
    public async Task<MessageNextResponse> NextRequestAsync(CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(_nextRequest, cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken);
    }
}

