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
    private readonly ConcurrentDictionary<ProtocolType, Channel<ReadOnlySequence<byte>>> _protocolChannels = [];
    private bool _isDisposed;

    /// <summary>
    /// Subscribes to a specific protocol channel.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A channel reader for consuming messages for the specified protocol.</returns>
    public ChannelReader<ReadOnlySequence<byte>> Subscribe(ProtocolType protocol)
    {
        if (!_protocolChannels.TryGetValue(protocol, out Channel<ReadOnlySequence<byte>>? channel))
        {
            channel = Channel.CreateBounded<ReadOnlySequence<byte>>(ProtocolConstants.MAX_CHANNEL_LOAD);
            _protocolChannels[protocol] = channel;
        }
        return channel.Reader;
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

    /// <summary>
    /// Creates a copy of the payload from a ReadOnlySequence.
    /// </summary>
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                MuxSegment segment = await ReadSegmentAsync(cancellationToken);

                if (_protocolChannels.TryGetValue(segment.Header.ProtocolId, out Channel<ReadOnlySequence<byte>>? channel))
                {
                    await channel.Writer.WriteAsync(segment.Payload, cancellationToken);
                }
                else
                {
                    // Protocol not subscribed - log or handle as needed
                    // For now we just drop the segment
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal cancellation, just exit
                break;
            }
            catch (Exception)
            {
                // Consider logging or handling errors
                // For now, we just continue to the next segment
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
        foreach (Channel<ReadOnlySequence<byte>> channel in _protocolChannels.Values)
        {
            channel.Writer.Complete();
        }

        // Don't dispose bearer here - it's provided externally

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}