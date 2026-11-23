using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
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
    private readonly ReadOnlyMemory<byte> _preEncodedNextRequest;
    private readonly ReplyLimit? _replies;

    /// <summary>
    /// Gets the protocol type for this ChainSync instance.
    /// </summary>
    public ProtocolType Protocol { get; }

    /// <summary>
    /// Gets or sets the current state of the protocol.
    /// </summary>
    public ChainSyncState State { get; private set; } = ChainSyncState.Idle;

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => State == ChainSyncState.Done;

    /// <summary>
    /// Gets whether the client has agency to send messages.
    /// </summary>
    public bool HasAgency => State == ChainSyncState.Idle;

    /// <summary>
    /// Initializes a new ChainSync instance with pre-encoded NextRequest for fast-path sending.
    /// </summary>
    public ChainSync(AgentChannel channel, ProtocolType protocol = ProtocolType.ClientChainSync)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _buffer = new ChannelBuffer(channel);
        Protocol = protocol;

        // Pre-encode the full mux segment for NextRequest at construction time.
        // This bypasses CBOR serialization and segment framing on every NextRequestAsync call.
        ChainSyncMessage nextRequest = ChainSyncMessages.NextRequest();
        byte[] cborPayload = CborSerializer.Serialize(nextRequest);
        _preEncodedNextRequest = BuildMuxSegment(protocol, cborPayload, channel.IsResponder);
    }

    internal ChainSync(AgentChannel channel, ProtocolType protocol, ReplyLimit? replies) : this(channel, protocol) => _replies = replies;

    /// <summary>
    /// Builds a complete mux segment (8-byte header + payload) ready for direct bearer write.
    /// </summary>
    private static byte[] BuildMuxSegment(ProtocolType protocolId, byte[] cborPayload, bool responder)
    {
        byte[] segment = new byte[ProtocolConstants.SegmentHeaderSize + cborPayload.Length];
        MuxSegmentCodec.WriteHeader(segment, 0, protocolId, checked((ushort)cborPayload.Length), responder);
        cborPayload.CopyTo(segment.AsSpan(ProtocolConstants.SegmentHeaderSize));
        return segment;
    }

    /// <summary>
    /// Finds an intersection point between the local and remote chains.
    /// On a <see cref="NodeClient"/> the reply is under <see cref="NodeClient.ReplyTimeLimit"/>.
    /// </summary>
    public async Task<ChainSyncMessage> FindIntersectionAsync(IEnumerable<Point> points, CancellationToken cancellationToken)
    {
        if (State != ChainSyncState.Idle)
        {
            throw new InvalidOperationException($"Cannot find intersection in state {State}");
        }

        Points message = new([.. points]);
        MessageFindIntersect messageCbor = ChainSyncMessages.FindIntersect(message);
        using ReplyLimit.Wait? wait = _replies?.Start(Protocol);
        await _buffer.SendFullMessageAsync<ChainSyncMessage>(messageCbor, cancellationToken).ConfigureAwait(false);

        State = ChainSyncState.Intersect;
        ChainSyncMessage response = await _buffer.ReceiveFullMessageAsync<ChainSyncMessage>(cancellationToken).ConfigureAwait(false);
        State = ChainSyncState.Idle;

        return response;
    }

    /// <summary>
    /// Requests the next update from the chain, properly handling the await state.
    /// Uses pre-encoded NextRequest for zero-allocation send path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<MessageNextResponse?> NextRequestAsync(CancellationToken cancellationToken)
    {
        MessageNextResponse? response = null;

        switch (State)
        {
            case ChainSyncState.Idle:
                // Fast-path: write pre-encoded segment directly to bearer
                await _buffer.SendPreEncodedSegmentAsync(_preEncodedNextRequest, cancellationToken).ConfigureAwait(false);
                State = ChainSyncState.CanAwait;
                response = await ReceiveNextResponseAsync(cancellationToken).ConfigureAwait(false);
                break;

            // CanAwait on entry: an earlier wait was cancelled or failed after the request went out, and its reply is still owed.
            case ChainSyncState.CanAwait:
            case ChainSyncState.MustReply:
                response = await ReceiveNextResponseAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ChainSyncState.Done:
                break;
            case ChainSyncState.Intersect:
            default:
                throw new InvalidOperationException($"Cannot request next in state {State}");
        }

        // Update state based on response. N2C and N2N RollForward follow the same agency rules.
        switch (response)
        {
            case MessageAwaitReply:
                State = ChainSyncState.MustReply;
                break;
            case N2CMessageRollForward:
            case N2NMessageRollForward:
            case MessageRollBackward:
                State = ChainSyncState.Idle;
                break;
            default:
                break;
        }

        return response;
    }

    /// <summary>
    /// Sends a single MsgRequestNext without waiting for a response.
    /// For use in pipelined sync where the caller manages in-flight count.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendNextRequestAsync(CancellationToken cancellationToken) => _buffer.SendPreEncodedSegmentAsync(_preEncodedNextRequest, cancellationToken);

    /// <summary>
    /// Sends N MsgRequestNext messages in a single batched write for pipelined sync.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> exceeds 6,554 requests in one batch. No requests are written.
    /// </exception>
    public Task SendNextRequestBatchAsync(int count, CancellationToken cancellationToken) => _buffer.SendPreEncodedSegmentBatchAsync(_preEncodedNextRequest, count, cancellationToken);

    /// <summary>
    /// Receives the next chain-sync response. A single <see cref="MessageNextResponse"/> union serves
    /// both N2C and N2N: its two index-2 RollForward members (<see cref="N2CMessageRollForward"/> and
    /// <see cref="N2NMessageRollForward"/>) are resolved by the serializer's structural probe on the
    /// payload shape. Deserialization reports the exact consumed length, keeping the buffer in sync.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<MessageNextResponse> ReceiveNextResponseAsync(CancellationToken cancellationToken) => _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken);

    /// <summary>
    /// Terminates the Chain-Sync protocol.
    /// </summary>
    public async Task DoneAsync(CancellationToken cancellationToken)
    {
        if (State != ChainSyncState.Idle)
        {
            throw new InvalidOperationException($"Cannot send done in state {State}");
        }

        MessageDone doneMessage = ChainSyncMessages.Done();
        await _buffer.SendFullMessageAsync<ChainSyncMessage>(doneMessage, cancellationToken).ConfigureAwait(false);
        State = ChainSyncState.Done;
    }
}
