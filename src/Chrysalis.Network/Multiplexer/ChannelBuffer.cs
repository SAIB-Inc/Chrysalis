using System.Buffers;
using System.IO.Pipelines;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Multiplexer;

public class ChannelBuffer(AgentChannel channel) : IDisposable
{
    private readonly AgentChannel _channel = channel;
    private readonly MemoryStream _buffer = new();
    private const int MAX_SEGMENT_PAYLOAD_LENGTH = 65535;

    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
    {
        byte[] payload = CborSerializer.Serialize(message);
        List<byte[]> payloadSplit = SplitPayloadIntoChunks(payload);
        foreach (byte[] chunk in payloadSplit)
        {
            await _channel.EnqueueChunkAsync(new(chunk), cancellationToken);
        }
    }

    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        PipeWriter bufferWriter = PipeWriter.Create(_buffer);
        while (true)
        {
            ReadOnlySequence<byte> chunk = await _channel.ReadChunkAsync(cancellationToken);
            bufferWriter.Write(chunk.FirstSpan);
            await bufferWriter.FlushAsync(cancellationToken);

            if (TryDecode(out T? message))
            {
                return message!;
            }
        }
    }

    public bool TryDecode<T>(out T? value) where T : CborBase
    {
        try
        {
            value = CborSerializer.Deserialize<T>(_buffer.ToArray());
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static List<byte[]> SplitPayloadIntoChunks(byte[] payload)
    {
        List<byte[]> chunks = [];

        for (int offset = 0; offset < payload.Length; offset += MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            int chunkSize = Math.Min(MAX_SEGMENT_PAYLOAD_LENGTH, payload.Length - offset);
            byte[] chunk = new byte[chunkSize];
            Array.Copy(payload, offset, chunk, 0, chunkSize);
            chunks.Add(chunk);
        }

        return chunks;
    }

    public void Dispose()
    {
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}