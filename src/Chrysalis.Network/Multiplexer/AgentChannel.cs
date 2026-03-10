using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents a bidirectional communication channel for a specific protocol.
/// </summary>
/// <remarks>
/// Read path uses System.Threading.Channels for segment dispatch from the demuxer.
/// Write path supports both the muxer pipeline (normal) and direct bearer writes (fast-path).
/// </remarks>
public sealed class AgentChannel(
    ProtocolType protocolId,
    PipeWriter muxerWriter,
    PipeWriter bearerWriter,
    ChannelReader<ReadOnlyMemory<byte>> demuxReader,
    SemaphoreSlim bearerWriteLock
)
{
    /// <summary>
    /// Enqueues a chunk of data through the muxer pipeline.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task EnqueueChunkAsync(ReadOnlySequence<byte> chunk, CancellationToken cancellationToken = default)
    {
        int payloadLength = (int)chunk.Length;

        // Protocol message: 1 byte protocol ID + 2 bytes length + payload
        Span<byte> protocolMessage = stackalloc byte[3 + payloadLength];

        protocolMessage[0] = (byte)protocolId;
        BinaryPrimitives.WriteUInt16BigEndian(protocolMessage[1..], (ushort)payloadLength);

        Span<byte> payloadSlice = protocolMessage.Slice(3, payloadLength);
        chunk.CopyTo(payloadSlice);

        muxerWriter.Write(protocolMessage);
        _ = await muxerWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a pre-encoded mux segment directly to the bearer, bypassing the muxer pipeline.
    /// Use for hot-path messages like NextRequest where the bytes never change.
    /// Guarded by a shared lock to prevent interleaving with Muxer writes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task WriteRawSegmentAsync(ReadOnlyMemory<byte> preEncodedSegment, CancellationToken cancellationToken = default)
    {
        await bearerWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await bearerWriter.WriteAsync(preEncodedSegment, cancellationToken).ConfigureAwait(false);
            _ = await bearerWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = bearerWriteLock.Release();
        }
    }

    /// <summary>
    /// Writes N copies of a pre-encoded segment in a single lock acquisition and flush.
    /// Used for pipelined ChainSync where many identical NextRequest messages are sent at once.
    /// </summary>
    public async Task WriteBatchRawSegmentsAsync(ReadOnlyMemory<byte> preEncodedSegment, int count, CancellationToken cancellationToken = default)
    {
        await bearerWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int i = 0; i < count; i++)
            {
                _ = await bearerWriter.WriteAsync(preEncodedSegment, cancellationToken).ConfigureAwait(false);
            }
            _ = await bearerWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = bearerWriteLock.Release();
        }
    }

    /// <summary>
    /// Reads the next available segment payload from this protocol channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ReadOnlyMemory<byte>> ReadSegmentAsync(CancellationToken cancellationToken = default) => demuxReader.ReadAsync(cancellationToken);

    /// <summary>
    /// Gets the protocol ID for this channel.
    /// </summary>
    public ProtocolType ProtocolId => protocolId;
}
