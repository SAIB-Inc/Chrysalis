using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;
using System.Threading;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// ChannelBuffer handles the chunking of messages that exceed the maximum segment size
/// and reassembles received chunks into complete messages for deserialization.
/// </remarks>
public sealed class ChannelBuffer(AgentChannel channel)
{
    // Reusable buffer pool to reduce allocations
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private const int INITIAL_BUFFER_SIZE = 4096;

    /// <summary>
    /// Sends a complete CBOR message, automatically chunking if needed.
    /// </summary>
    /// <typeparam name="T">The type of CBOR message to send.</typeparam>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the message has been sent.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(message);

        // Use standard serialization - the CborSerializer doesn't accept a buffer writer
        byte[] payload = CborSerializer.Serialize(message);
        
        ReadOnlyMemory<byte> payloadMemory = payload.AsMemory();
        int payloadLength = payload.Length;
        
        // Send the payload in chunks
        for (int offset = 0; offset < payloadLength; offset += ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int chunkSize = Math.Min(ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH, payloadLength - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            await channel.EnqueueChunkAsync(chunkSequence, cancellationToken);
        }
    }

    /// <summary>
    /// Receives a complete CBOR message, automatically handling chunked data.
    /// </summary>
    /// <typeparam name="T">The type of CBOR message to receive.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized message.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        byte[] buffer = _bufferPool.Rent(INITIAL_BUFFER_SIZE);
        int bufferLength = 0;
        
        try
        {
            // Keep reading chunks until we can successfully deserialize
            while (true)
            {
                // Read the next chunk first, instead of attempting deserialization prematurely
                ReadOnlySequence<byte> chunk = await channel.ReadChunkAsync(cancellationToken);
                
                // Ensure buffer is large enough
                if (bufferLength + chunk.Length > buffer.Length)
                {
                    // Need to resize - rent a new buffer with double capacity
                    byte[] newBuffer = _bufferPool.Rent(Math.Max(buffer.Length * 2, bufferLength + (int)chunk.Length));
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, bufferLength);
                    _bufferPool.Return(buffer);
                    buffer = newBuffer;
                }
                
                // Copy chunk to buffer
                chunk.CopyTo(buffer.AsSpan(bufferLength));
                bufferLength += (int)chunk.Length;
                
                try
                {
                    // Try to deserialize with current data
                    T result = CborSerializer.Deserialize<T>(buffer.AsMemory(0, bufferLength));
                    return result;
                }
                catch
                {
                    // Need more data, continue to next iteration
                }
            }
        }
        finally
        {
            // Always return the buffer to the pool
            _bufferPool.Return(buffer);
        }
    }

    /// <summary>
    /// Tries to receive a message with a timeout.
    /// </summary>
    /// <typeparam name="T">The type of CBOR message to receive.</typeparam>
    /// <param name="timeout">The maximum time to wait for a complete message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized message or null if the timeout expires.</returns>
    public async ValueTask<T?> TryReceiveWithTimeoutAsync<T>(TimeSpan timeout, CancellationToken cancellationToken) where T : CborBase
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await ReceiveFullMessageAsync<T>(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred
            return null;
        }
    }
}