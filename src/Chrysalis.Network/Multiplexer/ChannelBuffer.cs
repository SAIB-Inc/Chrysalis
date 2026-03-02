using System.Buffers;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// Uses a Pallas-style accumulation buffer: segment payloads are appended to a temp buffer,
/// CBOR decode is attempted after each append, and consumed bytes are drained on success.
/// Single-segment messages are deserialized directly from the received array (zero extra copy).
/// </remarks>
public sealed class ChannelBuffer(AgentChannel channel)
{
    // Accumulation buffer for partial CBOR messages (like Pallas's temp: Vec<u8>)
    private byte[] _temp = [];
    private int _tempOffset;
    private int _tempLength;

    /// <summary>
    /// Writes a pre-encoded mux segment directly to the bearer, bypassing serialization and muxer.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async Task SendPreEncodedSegmentAsync(ReadOnlyMemory<byte> preEncodedSegment, CancellationToken cancellationToken)
    {
        await channel.WriteRawSegmentAsync(preEncodedSegment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a complete CBOR message, chunked into segments if necessary.
    /// </summary>
    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
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
    /// Receives and deserializes a complete CBOR message, accumulating segment payloads as needed.
    /// </summary>
    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        // Try to decode from existing buffer first (may have leftover from previous message)
        int usedLength = _tempLength - _tempOffset;
        if (usedLength > 0)
        {
            if (TryDeserialize<T>(out T? result, out int consumed))
            {
                _tempOffset += consumed;
                CompactIfNeeded();
                return result!;
            }
        }

        // Read first chunk
        ReadOnlyMemory<byte> firstChunk = await channel.ReadSegmentAsync(cancellationToken).ConfigureAwait(false);

        // Fast path: if accumulation buffer is empty, try to deserialize directly from the chunk
        // This avoids copying into _temp for the common single-segment case
        if (usedLength == 0)
        {
            if (TryDeserializeDirect<T>(firstChunk, out T? result, out int consumed))
            {
                // If we consumed the entire chunk, we're done — no copy at all
                if (consumed >= firstChunk.Length)
                {
                    return result!;
                }

                // Leftover bytes — store them in _temp for the next message
                int leftover = firstChunk.Length - consumed;
                EnsureCapacity(leftover);
                firstChunk.Span.Slice(consumed, leftover).CopyTo(_temp.AsSpan(0, leftover));
                _tempOffset = 0;
                _tempLength = leftover;
                return result!;
            }
        }

        // Slow path: accumulate chunks until we can decode
        Append(firstChunk.Span);

        while (true)
        {
            if (TryDeserialize<T>(out T? result, out int consumed))
            {
                _tempOffset += consumed;
                CompactIfNeeded();
                return result!;
            }

            ReadOnlyMemory<byte> chunk = await channel.ReadSegmentAsync(cancellationToken).ConfigureAwait(false);
            Append(chunk.Span);
        }
    }

    /// <summary>
    /// Tries to deserialize directly from a ReadOnlyMemory without copying to _temp.
    /// </summary>
    private static bool TryDeserializeDirect<T>(ReadOnlyMemory<byte> data, out T? result, out int consumed) where T : CborBase
    {
        consumed = 0;
        result = default;

        if (data.Length == 0)
        {
            return false;
        }

        try
        {
            result = CborSerializer.Deserialize<T>(data, out consumed);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Appends bytes to the accumulation buffer.
    /// </summary>
    private void Append(ReadOnlySpan<byte> data)
    {
        int usedLength = _tempLength - _tempOffset;
        int required = usedLength + data.Length;
        EnsureCapacity(required);

        // Compact if we need to make room
        if (_tempOffset > 0 && _tempOffset + _tempLength - _tempOffset + data.Length > _temp.Length)
        {
            Compact();
        }

        data.CopyTo(_temp.AsSpan(_tempLength));
        _tempLength += data.Length;
    }

    /// <summary>
    /// Ensures the backing array is large enough for the given total data size.
    /// </summary>
    private void EnsureCapacity(int required)
    {
        if (required > _temp.Length)
        {
            int newSize = Math.Max(_temp.Length * 2, required);
            byte[] newBuffer = new byte[newSize];
            int usedLength = _tempLength - _tempOffset;
            if (usedLength > 0)
            {
                _temp.AsSpan(_tempOffset, usedLength).CopyTo(newBuffer);
            }
            _temp = newBuffer;
            _tempOffset = 0;
            _tempLength = usedLength;
        }
    }

    /// <summary>
    /// Compacts the buffer by moving data to the front if offset is too large.
    /// </summary>
    private void CompactIfNeeded()
    {
        // Compact when offset exceeds half the buffer or all data consumed
        int usedLength = _tempLength - _tempOffset;
        if (usedLength == 0)
        {
            _tempOffset = 0;
            _tempLength = 0;
        }
        else if (_tempOffset > _temp.Length / 2)
        {
            Compact();
        }
    }

    /// <summary>
    /// Moves used data to the front of the buffer.
    /// </summary>
    private void Compact()
    {
        int usedLength = _tempLength - _tempOffset;
        if (usedLength > 0)
        {
            _temp.AsSpan(_tempOffset, usedLength).CopyTo(_temp.AsSpan(0, usedLength));
        }
        _tempOffset = 0;
        _tempLength = usedLength;
    }

    /// <summary>
    /// Tries to deserialize a complete CBOR message from the accumulation buffer.
    /// </summary>
    private bool TryDeserialize<T>(out T? result, out int consumed) where T : CborBase
    {
        consumed = 0;
        result = default;

        int usedLength = _tempLength - _tempOffset;
        if (usedLength == 0)
        {
            return false;
        }

        try
        {
            ReadOnlyMemory<byte> data = new(_temp, _tempOffset, usedLength);
            result = CborSerializer.Deserialize<T>(data, out consumed);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
