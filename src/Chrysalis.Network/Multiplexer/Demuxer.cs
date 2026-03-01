using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Handles the demultiplexing of incoming data from a bearer into protocol-specific channels.
/// </summary>
/// <remarks>
/// The Demuxer reads segmented data from the network and routes it to the appropriate
/// protocol handler's channel based on the protocol ID in the segment header.
/// Uses System.Threading.Channels instead of Pipes for zero-copy dispatch.
/// </remarks>
public sealed class Demuxer(IBearer bearer) : IDisposable
{
    private readonly ConcurrentDictionary<ProtocolType, Channel<ReadOnlyMemory<byte>>> _protocolChannels = [];
    private bool _isDisposed;

    /// <summary>
    /// Subscribes to a specific protocol channel.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A channel reader for consuming segment payloads for the specified protocol.</returns>
    public ChannelReader<ReadOnlyMemory<byte>> Subscribe(ProtocolType protocol)
    {
        if (!_protocolChannels.TryGetValue(protocol, out Channel<ReadOnlyMemory<byte>>? channel))
        {
            channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(64)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            _protocolChannels[protocol] = channel;
        }
        return channel.Reader;
    }

    /// <summary>
    /// Runs the demuxer, continuously reading segments and dispatching them to the appropriate channels.
    /// Batches multiple segments per read for reduced async overhead.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? demuxerException = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Read whatever is available from the bearer
                ReadResult result = await bearer.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (result.IsCompleted && buffer.Length == 0)
                {
                    break;
                }

                SequencePosition consumed = buffer.Start;

                // Parse as many complete segments as possible from the buffer
                while (buffer.Length >= ProtocolConstants.SegmentHeaderSize)
                {
                    // Decode header
                    MuxSegmentHeader header = MuxSegmentCodec.DecodeHeader(buffer.Slice(0, ProtocolConstants.SegmentHeaderSize));

                    long totalSegmentSize = ProtocolConstants.SegmentHeaderSize + header.PayloadLength;
                    if (buffer.Length < totalSegmentSize)
                    {
                        break; // Incomplete segment, wait for more data
                    }

                    // Extract payload slice (zero-copy reference to PipeReader buffer)
                    ReadOnlySequence<byte> payloadSlice = buffer.Slice(ProtocolConstants.SegmentHeaderSize, header.PayloadLength);

                    // Dispatch to protocol channel
                    if (_protocolChannels.TryGetValue(header.ProtocolId, out Channel<ReadOnlyMemory<byte>>? channel))
                    {
                        // Copy payload to exact-sized owned array (no pool — ChannelBuffer takes ownership)
                        byte[] payloadCopy = GC.AllocateUninitializedArray<byte>(header.PayloadLength);
                        payloadSlice.CopyTo(payloadCopy);

                        await channel.Writer.WriteAsync(payloadCopy, cancellationToken).ConfigureAwait(false);
                    }

                    // Advance past this segment
                    buffer = buffer.Slice(totalSegmentSize);
                    consumed = buffer.Start;
                }

                bearer.Reader.AdvanceTo(consumed, buffer.End);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            demuxerException = ex;
            throw;
        }
        finally
        {
            CompleteChannels(demuxerException);
        }
    }

    /// <summary>
    /// Completes all protocol channels, optionally with an error state.
    /// </summary>
    private void CompleteChannels(Exception? error = null)
    {
        foreach (Channel<ReadOnlyMemory<byte>> channel in _protocolChannels.Values)
        {
            _ = channel.Writer.TryComplete(error);
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

        CompleteChannels();
        _isDisposed = true;
    }
}
