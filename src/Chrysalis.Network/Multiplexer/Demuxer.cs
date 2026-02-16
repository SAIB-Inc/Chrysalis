using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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
        // Read at least the header first
        ReadResult result = await bearer.Reader.ReadAtLeastAsync(ProtocolConstants.SegmentHeaderSize, cancellationToken);
        ReadOnlySequence<byte> buffer = result.Buffer;

        // Decode the header
        ReadOnlySequence<byte> headerSlice = buffer.Slice(0, ProtocolConstants.SegmentHeaderSize);
        MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(headerSlice);

        int totalRequired = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;

        // If we don't have enough data for header + payload, read more
        if (buffer.Length < totalRequired)
        {
            // Mark what we've examined (header) but haven't consumed anything yet
            bearer.Reader.AdvanceTo(buffer.Start, buffer.GetPosition(ProtocolConstants.SegmentHeaderSize));

            // Read until we have enough for header + payload
            result = await bearer.Reader.ReadAtLeastAsync(totalRequired, cancellationToken);
            buffer = result.Buffer;
        }

        // Now we have the complete segment in buffer
        ReadOnlySequence<byte> payloadSlice = buffer.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength);
        ReadOnlySequence<byte> payloadSequence = CopyPayload(payloadSlice);

        // Advance past the entire segment (header + payload)
        bearer.Reader.AdvanceTo(buffer.GetPosition(totalRequired));

        return new MuxSegment(header, payloadSequence);
    }

    /// <summary>
    /// Creates a copy of the payload from a ReadOnlySequence.
    /// </summary>
    /// <remarks>
    /// This method always allocates a new array because ReadOnlySequence doesn't track
    /// memory ownership, making it impossible to reliably return pooled buffers.
    /// For the typical segment sizes in Cardano protocols, this is acceptable.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySequence<byte> CopyPayload(ReadOnlySequence<byte> payload)
    {
        byte[] payloadCopy = new byte[payload.Length];
        payload.CopyTo(payloadCopy);
        return new ReadOnlySequence<byte>(payloadCopy);
    }

    /// <summary>
    /// Runs the demuxer, continuously reading segments and dispatching them to the appropriate channels.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? demuxerException = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                MuxSegment segment = await ReadSegmentAsync(cancellationToken);

                if (!_protocolPipes.TryGetValue(segment.Header.ProtocolId, out Pipe? pipe)) continue;

                await pipe.Writer.WriteAsync(segment.Payload.First, cancellationToken);
                await pipe.Writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation - don't treat as error
            throw;
        }
        catch (Exception ex)
        {
            // Capture exception to complete pipes with error state
            demuxerException = ex;
            throw;
        }
        finally
        {
            // Complete all pipes so waiting readers wake up
            CompletePipes(demuxerException);
        }
    }

    /// <summary>
    /// Completes all protocol pipes, optionally with an error state.
    /// </summary>
    /// <param name="error">The exception to propagate to pipe readers, or null for normal completion.</param>
    private void CompletePipes(Exception? error = null)
    {
        foreach (Pipe pipe in _protocolPipes.Values)
        {
            pipe.Writer.Complete(error);
        }
    }

    /// <summary>
    /// Disposes the resources used by the demuxer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        // Complete all pipes without error
        CompletePipes();

        // Don't dispose bearer here - it's provided externally

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}