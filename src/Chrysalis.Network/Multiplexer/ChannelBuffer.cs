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
        while (true)
        {
            // Read available data from the pipe
            ReadResult readResult = await channel.ReadChunkAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            // Try to deserialize
            try
            {
                // Attempt to deserialize directly from the ReadOnlySequence
                T result;

                if (buffer.IsSingleSegment)
                {
                    result = CborSerializer.Deserialize<T>(buffer.First);
                }
                else
                {
                    Memory<byte> subBuffer = new(ArrayPool<byte>.Shared.Rent((int)buffer.Length));
                    int bytesRead = 0;

                    foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                    {
                        Memory<byte> subBufferSlice = subBuffer.Slice(bytesRead, segment.Length);
                        segment.CopyTo(subBufferSlice);
                        bytesRead += subBufferSlice.Length;
                    }

                    result = CborSerializer.Deserialize<T>(subBuffer[..(int)buffer.Length]);
                }

                // Success! Mark all data as consumed
                channel.AdvanceTo(buffer.End);
                return result;
            }
            catch
            {
                // If we couldn't deserialize, we need more data
                // Mark what we've examined but couldn't use
                channel.AdvanceTo(buffer.Start);

                // If pipe is completed and we still can't deserialize, that's an error
                if (readResult.IsCompleted)
                    throw new InvalidOperationException("Pipe completed before a valid message could be parsed");
            }
        }
    }
}