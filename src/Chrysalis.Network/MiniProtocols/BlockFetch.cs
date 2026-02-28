using System.Runtime.CompilerServices;
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
    /// Receives blocks after a RequestRange has been sent.
    /// Transitions: Busy → Streaming → Idle (or Busy → Idle if NoBlocks).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of raw CBOR-encoded block bytes (zero-copy from CborEncodedValue).</returns>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveBlocksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
                    yield return UnwrapEncodedCbor(block.Body);
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
    /// <param name="from">The start point (inclusive).</param>
    /// <param name="to">The end point (inclusive).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of raw CBOR-encoded block bytes.</returns>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> FetchRangeAsync(
        Point from, Point to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RequestRangeAsync(from, to, cancellationToken).ConfigureAwait(false);
        await foreach (ReadOnlyMemory<byte> block in ReceiveBlocksAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return block;
        }
    }

    /// <summary>
    /// Fetches a single block at a specific point.
    /// </summary>
    /// <param name="point">The point identifying the block to fetch.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw CBOR-encoded block bytes, or null if the block was not found.</returns>
    public async Task<ReadOnlyMemory<byte>?> FetchSingleAsync(Point point, CancellationToken cancellationToken)
    {
        await RequestRangeAsync(point, point, cancellationToken).ConfigureAwait(false);

        ReadOnlyMemory<byte>? result = null;
        await foreach (ReadOnlyMemory<byte> block in ReceiveBlocksAsync(cancellationToken).ConfigureAwait(false))
        {
            result = block;
        }
        return result;
    }

    /// <summary>
    /// Unwraps a CBOR encoded value (tag 24 + byte string) to get the inner block bytes
    /// as a zero-copy slice of the original buffer.
    /// </summary>
    private static ReadOnlyMemory<byte> UnwrapEncodedCbor(CborEncodedValue encoded)
    {
        ReadOnlySpan<byte> span = encoded.Value.Span;
        int tagHeaderSize = GetCborTagHeaderSize(span);
        int bstrHeaderSize = GetCborByteStringHeaderSize(span[tagHeaderSize..]);
        return encoded.Value[(tagHeaderSize + bstrHeaderSize)..];
    }

    /// <summary>
    /// Computes the CBOR tag header size from the initial byte (major type 6).
    /// </summary>
    private static int GetCborTagHeaderSize(ReadOnlySpan<byte> data)
    {
        int additionalInfo = data[0] & 0x1F;
        return additionalInfo switch
        {
            < 24 => 1,
            24 => 2,
            25 => 3,
            26 => 5,
            27 => 9,
            _ => throw new FormatException($"Unexpected CBOR tag additional info: {additionalInfo}")
        };
    }

    /// <summary>
    /// Computes the CBOR byte string header size from the initial byte.
    /// </summary>
    private static int GetCborByteStringHeaderSize(ReadOnlySpan<byte> data)
    {
        // CBOR byte string: major type 2 (bits 7-5 = 010)
        // Additional info is bits 4-0
        byte initial = data[0];
        int additionalInfo = initial & 0x1F;
        return additionalInfo switch
        {
            < 24 => 1,       // Value is the additional info itself
            24 => 2,         // 1 byte follows
            25 => 3,         // 2 bytes follow (uint16)
            26 => 5,         // 4 bytes follow (uint32)
            27 => 9,         // 8 bytes follow (uint64)
            _ => throw new FormatException($"Unexpected CBOR additional info: {additionalInfo}")
        };
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
