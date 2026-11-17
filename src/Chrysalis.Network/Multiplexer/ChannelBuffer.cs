using System.Threading.Channels;
using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// Every returned message owns its bytes, as part of an item this buffer never reuses.
/// Caller-made channels must not modify items after queuing them; a message can be a slice of one.
/// </remarks>
public sealed class ChannelBuffer(AgentChannel channel)
{
    private ReadOnlyMemory<byte> _cursor;
    private readonly MessageAssembler? _assembler = channel.WholeMessages ? null : new(ProtocolLimits.Get(channel.ProtocolId));

    /// <summary>
    /// Writes a pre-encoded mux segment directly to the bearer, bypassing serialization and muxer.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task SendPreEncodedSegmentAsync(ReadOnlyMemory<byte> preEncodedSegment, CancellationToken cancellationToken) => channel.WriteRawSegmentAsync(preEncodedSegment, cancellationToken);

    /// <summary>
    /// Writes N copies of a pre-encoded mux segment in a single batched write.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task SendPreEncodedSegmentBatchAsync(ReadOnlyMemory<byte> preEncodedSegment, int count, CancellationToken cancellationToken) => channel.WriteBatchRawSegmentsAsync(preEncodedSegment, count, cancellationToken);

    /// <summary>
    /// Sends a complete CBOR message, chunked into segments if necessary.
    /// </summary>
    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborRecord
    {
        ReadOnlyMemory<byte> payloadMemory = CborSerializer.SerializeToMemory(message);
        int payloadLength = payloadMemory.Length;

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

    /// <summary>
    /// Receives and deserializes the next complete CBOR message.
    /// </summary>
    /// <remarks>Demuxer/Plexer channels supply whole-message items. Caller-made channels assemble
    /// their producer's items and preserve that reader's completion errors.</remarks>
    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborRecord
    {
        while (_cursor.IsEmpty)
        {
            _assembler?.ThrowIfFailed();
            ReadOnlyMemory<byte> item;
            // A transient reader error preserves partial state. Closed readers are terminal:
            // release scanner rentals while retaining the first failure for later receives.
            try { item = await channel.ReadSegmentAsync(cancellationToken).ConfigureAwait(false); }
            catch (ChannelClosedException error) { _ = _assembler?.Fail(error); throw; }
            _cursor = _assembler is null ? item : _assembler.Append(item, callerOwned: true);
        }
        T result = CborSerializer.Deserialize<T>(_cursor, out int consumed);
        _cursor = _cursor[consumed..];
        return result;
    }
}
