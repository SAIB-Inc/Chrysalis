using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Types;
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
/// <remarks>
/// Initializes a new instance of the ChainSync protocol.
/// </remarks>
/// <param name="channel">The channel for protocol communication.</param>
public class ChainSync(AgentChannel channel) : IMiniProtocol
{
    public static ProtocolType ProtocolType => ProtocolType.ClientChainSync;
    private readonly ChannelBuffer _buffer = new(channel);
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();
    private ChainSyncState _state = ChainSyncState.Idle;

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
            case MessageRollForward rollForward:
                _state = ChainSyncState.Idle;
                // For N2C, process the payload to strip CBOR tag if present
                return ProcessRollForward(rollForward);
            case MessageRollBackward:
                _state = ChainSyncState.Idle;
                break;
        }

        return response;
    }

    /// <summary>
    /// Process RollForward message to handle N2C CBOR tag wrapping.
    /// </summary>
    private MessageRollForward ProcessRollForward(MessageRollForward rollForward)
    {
        if (rollForward.Payload?.Value == null || rollForward.Payload.Value.Length == 0)
            return rollForward;

        try
        {
            // Check if payload starts with CBOR tag 24
            var reader = new CborReader(rollForward.Payload.Value, CborConformanceMode.Lax);
            
            if (reader.PeekState() == CborReaderState.Tag)
            {
                var tag = reader.ReadTag();
                if (tag == CborTag.EncodedCborDataItem)
                {
                    // Read the byte string and use that as the payload
                    var innerBytes = reader.ReadByteString();
                    return rollForward with { Payload = new CborEncodedValue(innerBytes) };
                }
            }
        }
        catch
        {
            // If anything fails, return original
        }
        
        return rollForward;
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