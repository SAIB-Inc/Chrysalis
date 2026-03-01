using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;
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
        _buffer = new ChannelBuffer(channel);
        Protocol = protocol;

        // Pre-encode the full mux segment for NextRequest at construction time.
        // This bypasses CborSerializer + Muxer pipeline on every NextRequestAsync call.
        ChainSyncMessage nextRequest = ChainSyncMessages.NextRequest();
        byte[] cborPayload = CborSerializer.Serialize(nextRequest);
        _preEncodedNextRequest = BuildMuxSegment(protocol, cborPayload);
    }

    /// <summary>
    /// Builds a complete mux segment (8-byte header + payload) ready for direct bearer write.
    /// </summary>
    private static byte[] BuildMuxSegment(ProtocolType protocolId, byte[] cborPayload)
    {
        // Header: 4 bytes timestamp + 2 bytes protocol ID + 2 bytes payload length
        byte[] segment = new byte[8 + cborPayload.Length];
        Span<byte> span = segment.AsSpan();

        // Timestamp = 0 (Cardano nodes don't validate it)
        BinaryPrimitives.WriteUInt32BigEndian(span[..4], 0);
        // Protocol ID (initiator mode, no 0x8000 bit)
        BinaryPrimitives.WriteUInt16BigEndian(span[4..6], (ushort)protocolId);
        // Payload length
        BinaryPrimitives.WriteUInt16BigEndian(span[6..8], (ushort)cborPayload.Length);
        // Payload
        cborPayload.CopyTo(span[8..]);

        return segment;
    }

    /// <summary>
    /// Finds an intersection point between the local and remote chains.
    /// </summary>
    public async Task<ChainSyncMessage> FindIntersectionAsync(IEnumerable<Point> points, CancellationToken cancellationToken)
    {
        if (State != ChainSyncState.Idle)
        {
            throw new InvalidOperationException($"Cannot find intersection in state {State}");
        }

        Points message = new([.. points]);
        MessageFindIntersect messageCbor = ChainSyncMessages.FindIntersect(message);
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
                response = await _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken).ConfigureAwait(false);
                break;

            case ChainSyncState.MustReply:
                response = await _buffer.ReceiveFullMessageAsync<MessageNextResponse>(cancellationToken).ConfigureAwait(false);
                break;
            case ChainSyncState.CanAwait:
                break;
            case ChainSyncState.Intersect:
                break;
            case ChainSyncState.Done:
                break;
            default:
                throw new InvalidOperationException($"Cannot request next in state {State}");
        }

        // Update state based on response
        switch (response)
        {
            case MessageAwaitReply:
                State = ChainSyncState.MustReply;
                break;
            case MessageRollForward:
                State = ChainSyncState.Idle;
                break;
            case MessageRollBackward:
                State = ChainSyncState.Idle;
                break;
            default:
                break;
        }

        return response;
    }

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
