using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Handles the demultiplexing of incoming data from a bearer into protocol-specific channels.
/// </summary>
/// <remarks>
/// The Demuxer reads segmented data from the network and routes it to the appropriate
/// protocol handler's channel based on the protocol ID in the segment header.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="Demuxer"/> class.
/// </remarks>
/// <param name="bearer">The bearer to read segments from.</param>
public sealed class Demuxer(IBearer bearer) : IDisposable
{
    private readonly ConcurrentDictionary<ProtocolType, Pipe> _protocolPipes = [];
    private bool _isDisposed;

    /// <summary>
    /// Subscribes to a specific protocol channel.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A channel reader for consuming messages for the specified protocol.</returns>
    public PipeReader Subscribe(ProtocolType protocol)
    {
        if (!_protocolPipes.TryGetValue(protocol, out Pipe? pipe))
        {
            pipe = new Pipe(
                new PipeOptions(
                    pool: MemoryPool<byte>.Shared,
                    minimumSegmentSize: 4096,
                    pauseWriterThreshold: 100 * 1024,
                    resumeWriterThreshold: 99 * 1024
                )
            );
            _protocolPipes[protocol] = pipe;
        }
        return pipe.Reader;
    }

    /// <summary>
    /// Reads a multiplexed segment from the bearer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The read segment.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<MuxSegment> ReadSegmentAsync(CancellationToken cancellationToken)
    {
        // Read the header
        ReadResult result = await bearer.Reader.ReadAtLeastAsync(ProtocolConstants.SEGMENT_HEADER_SIZE, cancellationToken);
        ReadOnlySequence<byte> buffer = result.Buffer;

        // Decode the header
        ReadOnlySequence<byte> headerSlice = buffer.Slice(0, ProtocolConstants.SEGMENT_HEADER_SIZE);
        MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(headerSlice);

        // Advance past the header
        bearer.Reader.AdvanceTo(buffer.GetPosition(ProtocolConstants.SEGMENT_HEADER_SIZE));

        // Read and process the payload
        ReadOnlySequence<byte> payloadSequence;

        if (buffer.Length >= header.PayloadLength + ProtocolConstants.SEGMENT_HEADER_SIZE)
        {
            // We already have the complete payload in the buffer
            ReadOnlySequence<byte> payloadSlice = buffer.Slice(ProtocolConstants.SEGMENT_HEADER_SIZE, header.PayloadLength);
            payloadSequence = CopyPayload(payloadSlice);
            bearer.Reader.AdvanceTo(buffer.GetPosition(header.PayloadLength + ProtocolConstants.SEGMENT_HEADER_SIZE));
        }
        else
        {
            // We need to read more data for the payload
            result = await bearer.Reader.ReadAtLeastAsync(header.PayloadLength, cancellationToken);
            ReadOnlySequence<byte> payloadSlice = result.Buffer.Slice(0, header.PayloadLength);
            payloadSequence = CopyPayload(payloadSlice);
            bearer.Reader.AdvanceTo(result.Buffer.GetPosition(header.PayloadLength));
        }

        return new MuxSegment(header, payloadSequence);
    }

    // Shared buffer pool to reduce allocations
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Creates a copy of the payload from a ReadOnlySequence using buffer pooling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySequence<byte> CopyPayload(ReadOnlySequence<byte> payload)
    {
        // For very small payloads, using pooling introduces more overhead than benefit
        if (payload.Length <= 128)
        {
            byte[] smallPayloadCopy = new byte[payload.Length];
            payload.CopyTo(smallPayloadCopy);
            return new ReadOnlySequence<byte>(smallPayloadCopy);
        }

        // For larger payloads, use the shared buffer pool
        byte[] payloadCopy = _bufferPool.Rent((int)payload.Length);

        try
        {
            payload.CopyTo(payloadCopy);
            // Store the buffer in the memory record for later return to pool
            PooledMemory managedPayload = new(payloadCopy, _bufferPool, (int)payload.Length);
            return new ReadOnlySequence<byte>(managedPayload.Memory);
        }
        catch
        {
            // Ensure buffer is returned on exception
            _bufferPool.Return(payloadCopy);
            throw;
        }
    }

    // Helper class to track pooled memory and return it when no longer used
    private sealed class PooledMemory(byte[] buffer, ArrayPool<byte> pool, int length) : IMemoryOwner<byte>
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
    /// Runs the demuxer, continuously reading segments and dispatching them to the appropriate channels.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                MuxSegment segment = await ReadSegmentAsync(cancellationToken);

                if (_protocolPipes.TryGetValue(segment.Header.ProtocolId, out Pipe? pipe))
                {
                    await pipe.Writer.WriteAsync(segment.Payload.First, cancellationToken);
                    await pipe.Writer.FlushAsync(cancellationToken);
                }
                else
                {
                    // Protocol not subscribed - log or handle as needed
                    // For now we just drop the segment
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Disposes the resources used by the demuxer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        // Complete all channels
        foreach (Pipe pipe in _protocolPipes.Values)
        {
            pipe.Writer.Complete();
        }

        // Don't dispose bearer here - it's provided externally

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}