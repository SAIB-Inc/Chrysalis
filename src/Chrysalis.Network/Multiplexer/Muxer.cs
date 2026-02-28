using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Handles multiplexing of protocol messages onto a shared transport.
/// </summary>
/// <remarks>
/// The Muxer takes messages from different protocol handlers and sends them
/// through a single bearer, adding appropriate headers for demultiplexing.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="Muxer"/> class.
/// </remarks>
/// <param name="bearerWriter">The pipe writer to write segments to.</param>
/// <param name="muxerMode">The mode of operation (initiator or responder).</param>
public sealed class Muxer(PipeWriter bearerWriter, ProtocolMode muxerMode) : IDisposable
{
    private readonly PipeWriter _bearerWriter = bearerWriter ?? throw new ArgumentNullException(nameof(bearerWriter));
    private readonly ProtocolMode _muxerMode = muxerMode;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly Pipe _pipe = new();
    private bool _isDisposed;

    /// <summary>
    /// Gets the channel writer for sending messages through the muxer.
    /// </summary>
    public PipeWriter Writer => _pipe.Writer;

    /// <summary>
    /// Writes a multiplexed segment to the bearer.
    /// </summary>
    /// <param name="segment">The segment to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task WriteSegmentAsync(MuxSegment segment, CancellationToken cancellationToken)
    {
        ReadOnlySequence<byte> encodedSegment = MuxSegmentCodec.Encode(segment);

        // Write directly using the sequence without creating an additional Memory/Span
        foreach (ReadOnlyMemory<byte> memory in encodedSegment)
        {
            _ = await _bearerWriter.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
        }

        // Flush to ensure data is sent
        _ = await _bearerWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the transmission time relative to the muxer start time.
    /// </summary>
    /// <returns>The transmission time in milliseconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CalculateTransmissionTime()
    {
        return (uint)Math.Min((DateTimeOffset.UtcNow - _startTime).TotalMilliseconds, uint.MaxValue);
    }

    /// <summary>
    /// Runs the muxer, continuously reading messages from the channel and writing them to the bearer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? muxerException = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult protocolMessageResult = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> protocolMessageBuffer = protocolMessageResult.Buffer;

                // protocol id
                ReadOnlySequence<byte> protocolIdSlice = protocolMessageBuffer.Slice(0, 1);
                ProtocolType protocolId = (ProtocolType)protocolIdSlice.FirstSpan[0];

                // payload length
                ReadOnlySequence<byte> payloadLengthSlice = protocolMessageBuffer.Slice(1, 2);
                ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(payloadLengthSlice.FirstSpan);

                // payload
                ReadOnlySequence<byte> payloadSlice = protocolMessageBuffer.Slice(3, payloadLength);

                MuxSegmentHeader segmentHeader = new(
                    TransmissionTime: CalculateTransmissionTime(),
                    ProtocolId: protocolId,
                    PayloadLength: (ushort)Math.Min(payloadSlice.Length, ProtocolConstants.MaxSegmentPayloadLength),
                    Mode: _muxerMode == ProtocolMode.Responder
                );

                MuxSegment segment = new(segmentHeader, payloadSlice);
                await WriteSegmentAsync(segment, cancellationToken).ConfigureAwait(false);
                _pipe.Reader.AdvanceTo(payloadSlice.End);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation - don't treat as error
            throw;
        }
        catch (Exception ex)
        {
            // Capture exception to complete pipe with error state
            muxerException = ex;
            throw;
        }
        finally
        {
            // Complete pipe so waiting writers are notified
            await _pipe.Writer.CompleteAsync(muxerException).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the resources used by the muxer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // Complete the channel
        _pipe.Writer.Complete();

        _isDisposed = true;
    }
}
