using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;

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

        byte[] payload = CborSerializer.Serialize(message);
        Memory<byte> payloadMemory = payload.AsMemory();

        for (int offset = 0; offset < payload.Length; offset += ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int chunkSize = Math.Min(ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH, payload.Length - offset);
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
    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        // Create a reusable buffer for receiving messages
        using MemoryStream buffer = new();

        while (true)
        {
            try
            {
                // Try to deserialize with current data
                buffer.Position = 0;
                return CborSerializer.Deserialize<T>(buffer.ToArray());
            }
            catch
            {
                // Need more data - read the next chunk
                ReadOnlySequence<byte> chunk = await channel.ReadChunkAsync(cancellationToken);

                // Reset position to end for appending
                buffer.Position = buffer.Length;

                // Copy chunk to our buffer
                foreach (ReadOnlyMemory<byte> memory in chunk)
                {
                    buffer.Write(memory.Span);
                }
            }
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