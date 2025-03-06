using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides minimal and highly optimized methods for encoding and decoding MuxSegments.
/// </summary>
/// <remarks>
/// The codec implements a binary protocol with the following header format:
/// - 4 bytes: Transmission time (uint32, big endian)
/// - 2 bytes: Protocol ID and mode flag (uint16, big endian, high bit = mode)
/// - 2 bytes: Payload length (uint16, big endian)
/// 
/// Performance is prioritized through aggressive inlining and avoiding struct copies.
/// </remarks>
public static class MuxSegmentCodec
{
    // Header size constants
    /// <summary>Size in bytes of the transmission time field.</summary>
    private const ushort TransmissionTimeSize = 4;

    /// <summary>Size in bytes of the protocol ID field.</summary>
    private const ushort ProtocolIdSize = 2;

    /// <summary>Size in bytes of the payload length field.</summary>
    private const ushort PayloadLengthSize = 2;

    /// <summary>Total size in bytes of the segment header.</summary>
    public const ushort HeaderSize = TransmissionTimeSize + ProtocolIdSize + PayloadLengthSize;

    // Buffer pool for encoding to reduce allocations
    private static readonly ArrayPool<byte> _encodeBufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Encodes a MuxSegment into a binary representation.
    /// </summary>
    /// <param name="segment">The segment to encode.</param>
    /// <returns>A sequence containing the binary representation of the segment.</returns>
    /// <remarks>
    /// The encoded format consists of an 8-byte header followed by the payload:
    /// - Bytes 0-3: Transmission time (uint32, big endian)
    /// - Bytes 4-5: Protocol ID with high bit representing mode (uint16, big endian)
    /// - Bytes 6-7: Payload length (uint16, big endian)
    /// - Bytes 8+: Payload data
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySequence<byte> Encode(in MuxSegment segment)
    {
        // Calculate total size needed
        int totalSize = HeaderSize + segment.Header.PayloadLength;

        // For small segments, avoid the pooling overhead
        if (totalSize <= 256)
        {
            // Create a result buffer directly - better for small segments
            byte[] result = new byte[totalSize];
            EncodeToBuffer(segment, result);
            return new ReadOnlySequence<byte>(result);
        }

        // For larger segments, use buffer pooling
        byte[] pooledBuffer = _encodeBufferPool.Rent(totalSize);

        try
        {
            EncodeToBuffer(segment, pooledBuffer);

            // Create a managed memory to ensure buffer is returned to pool
            var managedMemory = new EncodedMemory(pooledBuffer, _encodeBufferPool, totalSize);
            return new ReadOnlySequence<byte>(managedMemory.Memory);
        }
        catch
        {
            _encodeBufferPool.Return(pooledBuffer);
            throw;
        }
    }

    /// <summary>
    /// Helper method to encode segment data to a buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EncodeToBuffer(in MuxSegment segment, byte[] buffer)
    {
        // Write header
        Span<byte> headerSpan = buffer.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(headerSpan[..4], segment.Header.TransmissionTime);

        ushort protocolIdValue = (ushort)segment.Header.ProtocolId;
        if (segment.Header.Mode)
        {
            protocolIdValue |= 0x8000;  // Set high bit for responder mode
        }

        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(4, 2), protocolIdValue);
        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(6, 2), segment.Header.PayloadLength);

        // Write payload after header
        if (segment.Header.PayloadLength > 0 && !segment.Payload.IsEmpty)
        {
            Span<byte> payloadSpan = buffer.AsSpan(HeaderSize, segment.Header.PayloadLength);
            segment.Payload.CopyTo(payloadSpan);
        }
    }

    /// <summary>
    /// Helper class to track and dispose pooled memory
    /// </summary>
    private sealed class EncodedMemory(byte[] buffer, ArrayPool<byte> pool, int length) : IMemoryOwner<byte>
    {
        private readonly byte[] _buffer = buffer;
        private readonly ArrayPool<byte> _pool = pool;
        private readonly int _length = length;
        private bool _disposed = false;

        public Memory<byte> Memory => _buffer.AsMemory(0, _length);

        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Return(_buffer);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Decodes a binary sequence into a MuxSegmentHeader.
    /// </summary>
    /// <param name="headerSequence">The binary sequence containing the header data (must be at least 8 bytes).</param>
    /// <returns>The decoded MuxSegmentHeader.</returns>
    /// <remarks>
    /// This method extracts fields from the binary header:
    /// - Transmission time: 4-byte uint32 in big endian
    /// - Protocol ID: 15 lower bits of the 2-byte field in big endian
    /// - Mode flag: High bit (bit 15) of the protocol ID field
    /// - Payload length: 2-byte uint16 in big endian
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MuxSegmentHeader DecodeHeader(in ReadOnlySequence<byte> headerSequence)
    {
        uint transmissionTime = BinaryPrimitives.ReadUInt32BigEndian(headerSequence.Slice(0, 4).FirstSpan);
        ushort protocolIdAndMode = BinaryPrimitives.ReadUInt16BigEndian(headerSequence.Slice(4, 2).FirstSpan);
        bool mode = (protocolIdAndMode & 0x8000) != 0;         // Extract high bit for mode
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF);  // Extract lower 15 bits for protocol ID
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(headerSequence.Slice(6, 2).FirstSpan);

        return new(
            transmissionTime,
            protocolId,
            payloadLength,
            mode
        );
    }
}