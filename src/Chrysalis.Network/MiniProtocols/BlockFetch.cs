using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// BlockFetch protocol state machine states.
/// </summary>
public enum BlockFetchState
{
    /// <summary>Client has agency. Can send RequestRange or ClientDone.</summary>
    Idle,
    /// <summary>Server has agency. Will respond with StartBatch or NoBlocks.</summary>
    Busy,
    /// <summary>Server has agency. Will send Block messages followed by BatchDone.</summary>
    Streaming,
    /// <summary>Protocol is terminated.</summary>
    Done
}

/// <summary>
/// Implementation of the Ouroboros BlockFetch mini-protocol (N2N, channel 3).
/// </summary>
public class BlockFetch(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _buffer = new(channel);

    /// <summary>
    /// Gets the current state of the protocol.
    /// </summary>
    public BlockFetchState State { get; private set; } = BlockFetchState.Idle;

    /// <summary>
    /// Gets whether the protocol is terminated.
    /// </summary>
    public bool IsDone => State == BlockFetchState.Done;

    /// <summary>
    /// Gets whether the client has agency to send messages.
    /// </summary>
    public bool HasAgency => State == BlockFetchState.Idle;

    /// <summary>
    /// Sends a RequestRange message, transitioning from Idle to Busy.
    /// </summary>
    /// <param name="from">The start point (inclusive).</param>
    /// <param name="to">The end point (inclusive).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RequestRangeAsync(Point from, Point to, CancellationToken cancellationToken)
    {
        if (State != BlockFetchState.Idle)
        {
            throw new InvalidOperationException($"Cannot request range in state {State}");
        }

        RequestRange message = BlockFetchMessages.RequestRange(from, to);
        await _buffer.SendFullMessageAsync<BlockFetchMessage>(message, cancellationToken).ConfigureAwait(false);
        State = BlockFetchState.Busy;
    }

    /// <summary>
    /// Receives blocks after a RequestRange has been sent, deserializing each as <typeparamref name="T"/>.
    /// Transitions: Busy → Streaming → Idle (or Busy → Idle if NoBlocks).
    /// </summary>
    /// <typeparam name="T">The CBOR type to deserialize each block body as.</typeparam>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of deserialized block objects.</returns>
    public async IAsyncEnumerable<T> ReceiveBlocksAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : CborBase
    {
        if (State != BlockFetchState.Busy)
        {
            throw new InvalidOperationException($"Cannot receive blocks in state {State}");
        }

        BlockFetchMessage response = await _buffer
            .ReceiveFullMessageAsync<BlockFetchMessage>(cancellationToken)
            .ConfigureAwait(false);

        switch (response)
        {
            case NoBlocks:
                State = BlockFetchState.Idle;
                yield break;

            case StartBatch:
                State = BlockFetchState.Streaming;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unexpected message in Busy state: {response.GetType().Name}");
        }

        while (true)
        {
            BlockFetchMessage blockMsg = await _buffer
                .ReceiveFullMessageAsync<BlockFetchMessage>(cancellationToken)
                .ConfigureAwait(false);

            switch (blockMsg)
            {
                case BlockBody block:
                    yield return block.Body.DeserializeWithoutRaw<T>();
                    break;

                case BatchDone:
                    State = BlockFetchState.Idle;
                    yield break;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected message in Streaming state: {blockMsg.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Fetches all blocks in a range. Combines RequestRange + ReceiveBlocks.
    /// </summary>
    /// <typeparam name="T">The CBOR type to deserialize each block body as.</typeparam>
    /// <param name="from">The start point (inclusive).</param>
    /// <param name="to">The end point (inclusive).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of deserialized block objects.</returns>
    public async IAsyncEnumerable<T> FetchRangeAsync<T>(
        Point from, Point to,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : CborBase
    {
        await RequestRangeAsync(from, to, cancellationToken).ConfigureAwait(false);
        await foreach (T block in ReceiveBlocksAsync<T>(cancellationToken).ConfigureAwait(false))
        {
            yield return block;
        }
    }

    /// <summary>
    /// Fetches a single block at a specific point.
    /// </summary>
    /// <typeparam name="T">The CBOR type to deserialize the block body as.</typeparam>
    /// <param name="point">The point identifying the block to fetch.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized block, or null if the block was not found.</returns>
    public async Task<T?> FetchSingleAsync<T>(Point point, CancellationToken cancellationToken) where T : CborBase
    {
        await RequestRangeAsync(point, point, cancellationToken).ConfigureAwait(false);

        T? result = null;
        await foreach (T block in ReceiveBlocksAsync<T>(cancellationToken).ConfigureAwait(false))
        {
            result = block;
        }
        return result;
    }

    /// <summary>
    /// Terminates the BlockFetch protocol.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task DoneAsync(CancellationToken cancellationToken)
    {
        if (State != BlockFetchState.Idle)
        {
            throw new InvalidOperationException($"Cannot send done in state {State}");
        }

        ClientDone doneMessage = BlockFetchMessages.ClientDone();
        await _buffer.SendFullMessageAsync<BlockFetchMessage>(doneMessage, cancellationToken).ConfigureAwait(false);
        State = BlockFetchState.Done;
    }
}
