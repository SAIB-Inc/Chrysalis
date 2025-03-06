using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Handles multiplexing of protocol messages onto a shared transport.
/// </summary>
/// <remarks>
/// The Muxer takes messages from different protocol handlers and sends them
/// through a single bearer, adding appropriate headers for demultiplexing.
/// </remarks>
public sealed class Muxer : IDisposable
{
    private readonly IBearer _bearer;
    private readonly ProtocolMode _muxerMode;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly Channel<(ProtocolType ProtocolType, ReadOnlySequence<byte> Payload)> _channel;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Muxer"/> class.
    /// </summary>
    /// <param name="bearer">The bearer to write segments to.</param>
    /// <param name="muxerMode">The mode of operation (initiator or responder).</param>
    /// <exception cref="ArgumentNullException">Thrown if bearer is null.</exception>
    public Muxer(IBearer bearer, ProtocolMode muxerMode)
    {
        _bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
        _muxerMode = muxerMode;
        _channel = Channel.CreateBounded<(ProtocolType, ReadOnlySequence<byte>)>(
            new BoundedChannelOptions(ProtocolConstants.MAX_CHANNEL_LOAD)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <summary>
    /// Gets the channel writer for sending messages through the muxer.
    /// </summary>
    public ChannelWriter<(ProtocolType ProtocolType, ReadOnlySequence<byte> Payload)> Writer => _channel.Writer;

    /// <summary>
    /// Writes a multiplexed segment to the bearer.
    /// </summary>
    /// <param name="segment">The segment to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask WriteSegmentAsync(MuxSegment segment, CancellationToken cancellationToken)
    {
        ReadOnlySequence<byte> encodedSegment = MuxSegmentCodec.Encode(segment);
        
        // Write directly using the sequence without creating an additional Memory/Span
        foreach (ReadOnlyMemory<byte> memory in encodedSegment)
        {
            await _bearer.Writer.WriteAsync(memory, cancellationToken);
        }
        
        // Use ValueTask.WhenAll to potentially process multiple operations in parallel
        await _bearer.Writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Calculates the transmission time relative to the muxer start time.
    /// </summary>
    /// <returns>The transmission time in milliseconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CalculateTransmissionTime() =>
        (uint)Math.Min((DateTimeOffset.UtcNow - _startTime).TotalMilliseconds, uint.MaxValue);

    /// <summary>
    /// Runs the muxer, continuously reading messages from the channel and writing them to the bearer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    (ProtocolType protocolId, ReadOnlySequence<byte> payload) = await _channel.Reader.ReadAsync(cancellationToken);

                    MuxSegmentHeader segmentHeader = new MuxSegmentHeader(
                        TransmissionTime: CalculateTransmissionTime(),
                        ProtocolId: protocolId,
                        PayloadLength: (ushort)Math.Min(payload.Length, ProtocolConstants.MAX_SEGMENT_PAYLOAD_LENGTH),
                        Mode: _muxerMode == ProtocolMode.Responder
                    );

                    MuxSegment segment = new MuxSegment(segmentHeader, payload);
                    await WriteSegmentAsync(segment, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // @TODO: Logging
                }
            }
        }
        finally
        {
            // Ensure we clean up when the loop exits
            _channel.Writer.Complete();
        }
    }

    /// <summary>
    /// Disposes the resources used by the muxer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        // Complete the channel
        _channel.Writer.Complete();

        // Don't dispose bearer here - it's provided externally
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}