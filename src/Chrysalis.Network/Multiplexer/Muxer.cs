using System.Reactive.Subjects;
using System.Reactive.Linq;
using Chrysalis.Network.Core;
using System.Buffers;
using Microsoft.Extensions.ObjectPool;

namespace Chrysalis.Network.Multiplexer;

public class MuxSegmentPoolPolicy : IPooledObjectPolicy<MuxSegment>
{
    public MuxSegment Create() => new();

    public bool Return(MuxSegment obj)
    {
        obj.Reset();
        return true;
    }
}

/// <summary>
/// Multiplexes messages over a single bearer connection using functional asynchronous effects.
/// </summary>
/// <param name="bearer">The bearer connection for sending data.</param>
/// <param name="muxerMode">The mode of operation (Initiator or Responder).</param>
public class Muxer(IBearer bearer, ProtocolMode muxerMode) : IDisposable
{
    // Primary constructor parameters become fields.
    private readonly IBearer _bearer = bearer;
    private readonly ProtocolMode _muxerMode = muxerMode;

    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly ReplaySubject<ProtocolMessage> _messageSubject = new(bufferSize: 1);
    private readonly CancellationTokenSource _muxerCts = new();
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly ObjectPool<MuxSegment> _segmentPool = new DefaultObjectPool<MuxSegment>(
        new MuxSegmentPoolPolicy(),
        maximumRetained: 32);

    /// <summary>
    /// Gets the subject for sending messages.
    /// </summary>
    /// <returns>The message subject to which messages can be pushed.</returns>
    public ISubject<ProtocolMessage> GetMessageSubject() => _messageSubject;

    /// <summary>
    /// Encodes a protocol message into a mux segment and sends it over the bearer.
    /// This method composes pure computations (segment creation and encoding) with effectful sends.
    /// </summary>
    /// <param name="message">The protocol message to send.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding Unit upon successful sending or capturing an exception on failure.
    /// </returns>
    public Aff<Unit> WriteSegment(ProtocolMessage message, CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            // Get a segment from the pool
            MuxSegment segment = _segmentPool.Get();
            try
            {
                // Configure the segment
                segment.TransmissionTime = (uint)(DateTimeOffset.UtcNow - _startTime).TotalMilliseconds;
                segment.ProtocolId = message.Protocol;
                segment.PayloadLength = (ushort)message.Payload.Length;
                segment.Payload = message.Payload;  // Note: we're borrowing the reference, not copying
                segment.Mode = _muxerMode == ProtocolMode.Responder;

                // Calculate max buffer size needed (header + payload)
                int maxBufferSize = 8 + message.Payload.Length;

                // Rent a buffer from the pool
                byte[] encodedBuffer = _bufferPool.Rent(maxBufferSize);

                try
                {
                    // Encode directly into the rented buffer
                    int bytesWritten = MuxSegmentCodec.EncodeInto(segment, encodedBuffer.AsSpan());
                    // Send only the portion we filled
                    await _bearer.Send(encodedBuffer.AsMemory(0, bytesWritten), cancellationToken).Run();

                    return unit;
                }
                finally
                {
                    // Return the buffer to the pool
                    _bufferPool.Return(encodedBuffer);
                }
            }
            finally
            {
                // Return the segment to the pool
                _segmentPool.Return(segment);
            }
        });
    }

    /// <summary>
    /// Runs the multiplexing process, continuously processing messages from the subject.
    /// This method wraps the reactive loop into an Aff monad.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding Unit when processing completes or cancellation is triggered.
    /// </returns>
    public Aff<Unit> Run(CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            // Process messages in batches when possible
            await _messageSubject.AsObservable()
                .Buffer(TimeSpan.FromMilliseconds(5), 10) // Buffer up to 10 messages or for 5ms
                .Where(batch => batch.Count > 0)
                .ForEachAsync(async messageBatch =>
                {
                    foreach (var message in messageBatch)
                    {
                        await WriteSegment(message, cancellationToken).Run();
                    }
                }, cancellationToken);

            return unit;
        });
    }

    /// <summary>
    /// Disposes of the muxer, cancelling the processing loop and releasing resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (!_muxerCts.IsCancellationRequested)
            _muxerCts.Cancel();
        _messageSubject.OnCompleted();
        _messageSubject.Dispose();
        _muxerCts.Dispose();
    }
}
