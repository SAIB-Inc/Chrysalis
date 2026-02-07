using System.Runtime.CompilerServices;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Point = Chrysalis.Network.Cbor.Common.Point;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// ChainSync protocol state machine states.
/// </summary>
public enum ChainSyncState
{
    /// <summary>Client has agency and can send requests.</summary>
    Idle,
    /// <summary>Waiting for response, server can send AwaitReply, RollForward, or RollBackward.</summary>
    CanAwait,
    /// <summary>Server has agency and will send RollForward or RollBackward when ready.</summary>
    MustReply,
    /// <summary>Waiting for intersection response.</summary>
    Intersect,
    /// <summary>Protocol is terminated.</summary>
    Done
}

/// <summary>
/// Implementation of the Ouroboros ChainSync mini-protocol.
/// </summary>
public class ChainSync : IMiniProtocol
{
    private readonly ChannelBuffer _buffer;
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();
    private ChainSyncState _state = ChainSyncState.Idle;

    public ChainSync(AgentChannel channel, ProtocolType protocol = ProtocolType.ClientChainSync)
    {
        _buffer = new ChannelBuffer(channel);
        Protocol = protocol;
    }

    /// <summary>
    /// Gets the protocol type for this ChainSync instance.
    /// </summary>
    public ProtocolType Protocol { get; }

    /// <summary>
    /// Gets the current state of the protocol.
    /// </summary>
    public ChainSyncState State => _state;

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => _state == ChainSyncState.Done;

    /// <summary>
    /// Gets whether the client has agency to send messages.
    /// </summary>
    public bool HasAgency => _state == ChainSyncState.Idle;

    /// <summary>
    /// Finds an intersection point between the local and remote chains.
    /// </summary>
    /// <param name="points">Collection of points to check for intersection.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message indicating intersection status.</returns>
    public async Task<ChainSyncMessage> FindIntersectionAsync(IEnumerable<Point> points, CancellationToken cancellationToken)
    {
        if (_state != ChainSyncState.Idle)
            throw new InvalidOperationException($"Cannot find intersection in state {_state}");

        Points message = new([.. points]);
        MessageFindIntersect messageCbor = ChainSyncMessages.FindIntersect(message);
        await _buffer.SendFullMessageAsync<ChainSyncMessage>(messageCbor, cancellationToken);
        
        _state = ChainSyncState.Intersect;
        var response = await _buffer.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken);
        _state = ChainSyncState.Idle;
        
        return response;
    }

    /// <summary>
    /// Requests the next update from the chain, properly handling the await state.
    /// Safe to call repeatedly in a loop - handles protocol state machine internally.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The next update response.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<MessageNextResponse?> NextRequestAsync(CancellationToken cancellationToken)
    {
        MessageNextResponse response;

        switch (_state)
        {
            case ChainSyncState.Idle:
                // We have agency, send request
                await _buffer.SendFullMessageAsync(_nextRequest, cancellationToken);
                _state = ChainSyncState.CanAwait;
                response = await _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken);
                break;

            case ChainSyncState.MustReply:
                // Server has agency, just wait for response
                response = await _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Cannot request next in state {_state}");
        }

        // Update state based on response
        switch (response)
        {
            case MessageAwaitReply:
                _state = ChainSyncState.MustReply;
                break;
            case MessageRollForward:
                _state = ChainSyncState.Idle;
                break;
            case MessageRollBackward:
                _state = ChainSyncState.Idle;
                break;
        }

        return response;
    }


    /// <summary>
    /// Terminates the Chain-Sync protocol.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DoneAsync(CancellationToken cancellationToken)
    {
        if (_state != ChainSyncState.Idle)
            throw new InvalidOperationException($"Cannot send done in state {_state}");

        var doneMessage = ChainSyncMessages.Done();
        await _buffer.SendFullMessageAsync<ChainSyncMessage>(doneMessage, cancellationToken);
        _state = ChainSyncState.Done;
    }
}
