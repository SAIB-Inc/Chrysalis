using System;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Threading;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Multiplexes messages over a single bearer connection using functional asynchronous effects.
/// </summary>
/// <param name="bearer">The bearer connection for sending data.</param>
/// <param name="muxerMode">The mode of operation (Initiator or Responder).</param>
public class Muxer(IBearer bearer, ProtocolMode muxerMode) : IDisposable
{
    // Primary constructor parameters become fields.
    private readonly IBearer bearer = bearer;
    private readonly ProtocolMode muxerMode = muxerMode;
    
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly Subject<ProtocolMessage> _messageSubject = new();
    private readonly CancellationTokenSource _muxerCts = new();

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
    public Aff<Unit> WriteSegment(ProtocolMessage message, CancellationToken cancellationToken) =>
        // Create the mux segment as a pure computation.
        Aff(() => ValueTask.FromResult(
            new MuxSegment(
                TransmissionTime: (uint)(DateTimeOffset.UtcNow - _startTime).TotalMilliseconds,
                ProtocolId: message.Protocol,
                PayloadLength: (ushort)message.Payload.Length,
                Payload: message.Payload,
                Mode: muxerMode == ProtocolMode.Responder)))
        // Encode the mux segment using our functional encoder wrapped in a ValueTask.
        .Bind(segment =>
            Aff(() => ValueTask.FromResult(MuxSegmentCodec.TryEncode(segment).IfFailThrow())))
        // Send the encoded segment over the bearer.
        .Bind(encodedSegment => bearer.SendAsync(encodedSegment, cancellationToken));

    /// <summary>
    /// Runs the multiplexing process, continuously processing messages from the subject.
    /// This method wraps the reactive loop into an Aff monad.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding Unit when processing completes or cancellation is triggered.
    /// </returns>
    public Aff<Unit> Run(CancellationToken cancellationToken) =>
        Aff(async () =>
        {
            await GetMessageSubject().AsObservable()
                .ForEachAsync(async message =>
                {
                    // Execute the WriteSegment effect for each incoming message.
                    await WriteSegment(message, cancellationToken).Run();
                }, cancellationToken);
            return unit;
        });

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
