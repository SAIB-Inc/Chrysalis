using System.Reactive.Subjects;
using System.Reactive.Linq;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public enum ProtocolMode
{
    Initiator,
    Responder
}
/// <summary>
/// Multiplexes messages over a single bearer connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Muxer"/> class.
/// </remarks>
/// <param name="bearer">The bearer connection for sending data.</param>
/// <param name="muxerMode">The mode of operation (Initiator or Responder).</param>
public class Muxer(IBearer bearer, ProtocolMode muxerMode) : IDisposable
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly Subject<ProtocolMessage> _messageSubject = new();
    private readonly CancellationTokenSource _muxerCts = new();

    /// <summary>
    /// Gets the subject for sending messages.
    /// </summary>
    /// <returns>The message subject.</returns>
    public ISubject<ProtocolMessage> GetMessageSubject() => _messageSubject;

    /// <summary>
    /// Writes a message as a mux segment and sends it over the bearer.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task WriteSegmentAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        uint timestamp = (uint)(DateTimeOffset.UtcNow - _startTime).TotalMilliseconds;
        MuxSegment segment = new(
            TransmissionTime: timestamp,
            ProtocolId: message.Protocol,
            PayloadLength: (ushort)message.Payload.Length,
            Payload: message.Payload,
            Mode: muxerMode == ProtocolMode.Responder
        );

        byte[] encodedSegment = MuxSegmentCodec.Encode(segment);
        await bearer.SendAsync(encodedSegment, cancellationToken);
    }

    /// <summary>
    /// Runs the multiplexing process, processing messages from the subject.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _messageSubject.AsObservable()
            .ForEachAsync(
                async message => await WriteSegmentAsync(message, cancellationToken),
                cancellationToken
            );
    }

    /// <summary>
    /// Disposes of the muxer, releasing all resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (!_muxerCts.IsCancellationRequested)
        {
            _muxerCts.Cancel();
        }

        _messageSubject.OnCompleted();
        _messageSubject.Dispose();
        _muxerCts.Dispose();
    }
}
