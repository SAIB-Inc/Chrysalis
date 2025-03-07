using System.Buffers;
using System.IO.Pipelines;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ChannelBuffer class.
/// </remarks>
/// <param name="channel">The channel to buffer data for.</param>
public sealed class ChannelBuffer(AgentChannel channel)
{
    private readonly AgentChannel _channel = channel;

    /// <summary>
    /// Sends a complete CBOR message, automatically chunking if needed.
    /// </summary>
    /// <typeparam name="T">The type of CBOR message to send.</typeparam>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async ValueTask SendFullMessageAsync<T>(T message, CancellationToken cancellationToken = default) where T : CborBase
    {
        byte[] payload = CborSerializer.Serialize(message);
        ReadOnlyMemory<byte> payloadMemory = payload.AsMemory();
        int payloadLength = payload.Length;

        for (int offset = 0; offset < payloadLength; offset += ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int chunkSize = Math.Min(ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH, payloadLength - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            await _channel.EnqueueChunkAsync(chunkSequence, cancellationToken);
        }
    }

    /// <summary>
    /// Receives a complete CBOR message, automatically handling chunked data.
    /// </summary>
    /// <typeparam name="T">The type of CBOR message to receive.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized message.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pipe completes before a valid message could be parsed.</exception>
    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken = default) where T : CborBase
    {
        while (true)
        {
            ReadResult readResult = await _channel.ReadChunkAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            try
            {
                T result;
                if (buffer.IsSingleSegment)
                {
                    result = CborSerializer.Deserialize<T>(buffer.First);
                    _channel.AdvanceTo(buffer.End);
                    return result;
                }

                // Buffer is multi-segment, need to copy to contiguous memory
                byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                try
                {
                    buffer.CopyTo(rentedBuffer);
                    result = CborSerializer.Deserialize<T>(rentedBuffer.AsMemory(0, (int)buffer.Length));
                    _channel.AdvanceTo(buffer.End);
                    return result;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
            catch (Exception) when (!readResult.IsCompleted)
            {
                // Need more data - mark what we examined but couldn't use
                _channel.AdvanceTo(buffer.Start);
            }
            catch (Exception) when (readResult.IsCompleted)
            {
                // If pipe is completed and we still can't deserialize, that's an error
                _channel.AdvanceTo(buffer.End);
                throw new InvalidOperationException("Pipe completed before a valid message could be parsed");
            }
        }
    }
}