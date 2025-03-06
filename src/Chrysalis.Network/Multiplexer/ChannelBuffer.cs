using System.Buffers;
using System.IO.Pipelines;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public class ChannelBuffer(AgentChannel channel)
{
    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
    {
        byte[] payload = CborSerializer.Serialize(message);
        Memory<byte> payloadMemory = payload.AsMemory();

        for (int offset = 0; offset < payload.Length; offset += ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            int chunkSize = Math.Min(ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH, payload.Length - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            await channel.EnqueueChunkAsync(chunkSequence, cancellationToken);
        }
    }

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
}