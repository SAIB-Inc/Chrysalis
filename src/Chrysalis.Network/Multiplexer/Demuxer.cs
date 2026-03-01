using System.Buffers;
using System.Buffers.Binary;
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
        while (true)
        {
            ReadResult result = await bearer.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                throw new InvalidOperationException("Connection closed before a complete mux segment was received.");
            }

            if (!TryDecodeHeader(buffer, out MuxSegmentHeader header))
            {
                if (result.IsCompleted)
                {
                    bearer.Reader.AdvanceTo(buffer.End);
                    throw new InvalidOperationException("Connection closed with an incomplete mux segment header.");
                }

                bearer.Reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            long totalSegmentLength = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
            if (buffer.Length < totalSegmentLength)
            {
                if (result.IsCompleted)
                {
                    bearer.Reader.AdvanceTo(buffer.End);
                    throw new InvalidOperationException("Connection closed with an incomplete mux segment payload.");
                }

                bearer.Reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            ReadOnlySequence<byte> payloadSlice = buffer.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength);
            byte[] payloadCopy = header.PayloadLength == 0
                ? []
                : GC.AllocateUninitializedArray<byte>(header.PayloadLength);

            if (header.PayloadLength > 0)
            {
                payloadSlice.CopyTo(payloadCopy);
            }

            bearer.Reader.AdvanceTo(buffer.GetPosition(totalSegmentLength));
            return new MuxSegment(header, new ReadOnlySequence<byte>(payloadCopy));
        }
    }

    /// <summary>
    /// Runs the demuxer, continuously reading segments and dispatching them to the appropriate channels.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? demuxerException = null;
        HashSet<PipeWriter> writersToFlush = [];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await bearer.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition consumed = buffer.Start;

                while (TryReadNextSegment(buffer.Slice(consumed), out MuxSegmentHeader header, out ReadOnlySequence<byte> payload, out SequencePosition nextConsumed))
                {
                    consumed = nextConsumed;

                    if (!_protocolPipes.TryGetValue(header.ProtocolId, out Pipe? pipe))
                    {
                        continue;
                    }

                    WritePayload(pipe.Writer, payload);
                    _ = writersToFlush.Add(pipe.Writer);
                }

                bearer.Reader.AdvanceTo(consumed, buffer.End);

                if (result.IsCompleted && !buffer.Slice(consumed).IsEmpty)
                {
                    throw new InvalidOperationException("Connection closed with incomplete mux segment data.");
                }

                foreach (PipeWriter writer in writersToFlush)
                {
                    _ = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                writersToFlush.Clear();

                if (result.IsCompleted)
                {
                    break;
                }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePayload(PipeWriter writer, ReadOnlySequence<byte> payload)
    {
        foreach (ReadOnlyMemory<byte> chunk in payload)
        {
            writer.Write(chunk.Span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadNextSegment(
        ReadOnlySequence<byte> buffer,
        out MuxSegmentHeader header,
        out ReadOnlySequence<byte> payload,
        out SequencePosition nextConsumed
    )
    {
        payload = default;
        nextConsumed = buffer.Start;

        if (!TryDecodeHeader(buffer, out header))
        {
            return false;
        }

        long totalSegmentLength = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
        if (buffer.Length < totalSegmentLength)
        {
            return false;
        }

        payload = buffer.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength);
        nextConsumed = buffer.GetPosition(totalSegmentLength);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDecodeHeader(ReadOnlySequence<byte> buffer, out MuxSegmentHeader header)
    {
        header = null!;

        if (buffer.Length < ProtocolConstants.SegmentHeaderSize)
        {
            return false;
        }

        Span<byte> headerBytes = stackalloc byte[ProtocolConstants.SegmentHeaderSize];
        buffer.Slice(0, ProtocolConstants.SegmentHeaderSize).CopyTo(headerBytes);

        uint transmissionTime = BinaryPrimitives.ReadUInt32BigEndian(headerBytes[..4]);
        ushort protocolIdAndMode = BinaryPrimitives.ReadUInt16BigEndian(headerBytes.Slice(4, 2));
        bool mode = (protocolIdAndMode & 0x8000) != 0;
        ProtocolType protocolId = (ProtocolType)(protocolIdAndMode & 0x7FFF);
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(headerBytes.Slice(6, 2));

        header = new MuxSegmentHeader(
            transmissionTime,
            protocolId,
            payloadLength,
            mode
        );
        return true;
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
        if (_isDisposed)
        {
            return;
        }

        // Complete all pipes without error
        CompletePipes();

        // Don't dispose bearer here - it's provided externally

        _isDisposed = true;
    }
}
