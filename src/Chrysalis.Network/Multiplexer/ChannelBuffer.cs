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
    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
    {
        byte[] payload = CborSerializer.Serialize(message);
        ReadOnlyMemory<byte> payloadMemory = payload.AsMemory();
        int payloadLength = payload.Length;

        for (int offset = 0; offset < payloadLength; offset += ProtocolConstants.MaxSegmentPayloadLength)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            int chunkSize = Math.Min(ProtocolConstants.MaxSegmentPayloadLength, payloadLength - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            await channel.EnqueueChunkAsync(chunkSequence, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        while (true)
        {
            ReadResult readResult = await channel.ReadChunkAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            // Check if pipe was completed (demuxer stopped) before we have a complete message
            if (readResult.IsCompleted && buffer.Length == 0)
            {
                throw new InvalidOperationException(
                    "Connection closed by multiplexer. The demuxer has stopped, likely due to a network error or node disconnection. " +
                    "Check the plexer health status for the underlying exception."
                );
            }

            try
            {
                T result;
                if (buffer.IsSingleSegment)
                {
                    result = CborSerializer.Deserialize<T>(buffer.First);
                    channel.AdvanceTo(buffer.End);
                    return result;
                }

                // Buffer is multi-segment, need to copy to contiguous memory
                byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                try
                {
                    buffer.CopyTo(rentedBuffer);
                    result = CborSerializer.Deserialize<T>(rentedBuffer.AsMemory(0, (int)buffer.Length));
                    channel.AdvanceTo(buffer.End);
                    return result;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
            catch (FormatException) when (!readResult.IsCompleted)
            {
                // Need more data - mark what we examined but couldn't use
                channel.AdvanceTo(buffer.Start);
            }
            catch (FormatException) when (readResult.IsCompleted)
            {
                // If pipe is completed and we still can't deserialize, that's an error
                channel.AdvanceTo(buffer.End);
                throw new InvalidOperationException(
                    "Pipe completed with partial data that could not be deserialized. " +
                    "This may indicate connection was closed mid-message."
                );
            }
        }
    }
}
