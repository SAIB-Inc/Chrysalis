using System.Buffers;
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
/// <param name="bearerWriteLock">Shared lock to coordinate bearer writes with AgentChannel fast-path.</param>
public sealed class Muxer(PipeWriter bearerWriter, ProtocolMode muxerMode, SemaphoreSlim bearerWriteLock) : IDisposable
{
    private readonly PipeWriter _bearerWriter = bearerWriter ?? throw new ArgumentNullException(nameof(bearerWriter));
    private readonly ProtocolMode _muxerMode = muxerMode;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    // AvailableWaitHandle is never used, so these semaphores own no wait handles
    // to dispose. Each entry (and its semaphore) dies with its PipeWriter.
    internal static readonly ConditionalWeakTable<PipeWriter, WriterState> WriterStates = [];

    private readonly Pipe _pipe = CreatePipe();
    // Once started, RunAsync owns consumer completion; Dispose only requests
    // cancellation. Before start, Dispose owns completion of both pipe ends.
    // Publish stop, complete Reader to release pending flushes, then take the
    // writer permit and complete Writer. Never dispose a permit with waiters.
    // The lifecycle lock also keeps cancellation-source disposal out of a race
    // with Dispose starting its asynchronous cancellation callbacks.
    private readonly Lock _lifecycleLock = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private Task _shutdownTask = Task.CompletedTask;
    private bool _runStarted;
    private bool _shutdownCompleted;
    private bool _isDisposed;

    internal sealed class WriterState
    {
        private bool _stopped;
        private Exception? _error;
        internal SemaphoreSlim WriteLock { get; } = new(1, 1);

        // Muxer calls this under its lifecycle lock. Publish the cause before
        // the stopped flag so senders always observe the first shutdown cause.
        internal Exception? Stop(Exception? error)
        {
            if (!_stopped)
            {
                _error = error;
                Volatile.Write(ref _stopped, true);
            }
            return _error;
        }

        internal void ThrowIfStopped()
        {
            if (Volatile.Read(ref _stopped))
            {
                throw new InvalidOperationException("The muxer has stopped accepting protocol messages.", _error);
            }
        }
    }

    private WriterState State => WriterStates.GetValue(_pipe.Writer, static _ => new WriterState());

    private CancellationTokenSource CreateRunCancellation(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_runStarted)
            {
                throw new InvalidOperationException("The muxer can only be run once.");
            }
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCancellation.Token);
            _runStarted = true;
            return linked;
        }
    }

    private static Pipe CreatePipe()
    {
        Pipe pipe = new();
        // Register before Writer can be handed to any AgentChannel, including
        // callers using its public constructor rather than Plexer.SubscribeClient.
        _ = WriterStates.GetValue(pipe.Writer, static _ => new WriterState());
        return pipe;
    }

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

        await bearerWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (ReadOnlyMemory<byte> memory in encodedSegment)
            {
                _ = await _bearerWriter.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
            }

            _ = await _bearerWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = bearerWriteLock.Release();
        }
    }

    /// <summary>
    /// Calculates the transmission time relative to the muxer start time.
    /// </summary>
    /// <returns>The transmission time in milliseconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CalculateTransmissionTime() => (uint)Math.Min((DateTimeOffset.UtcNow - _startTime).TotalMilliseconds, uint.MaxValue);

    /// <summary>
    /// Runs the muxer, continuously reading messages from the channel and writing them to the bearer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource runCancellation = CreateRunCancellation(cancellationToken);
        CancellationToken runToken = runCancellation.Token;
        WriterState state = State;
        Exception? muxerException = null;

        try
        {
            runToken.ThrowIfCancellationRequested();
            while (!runToken.IsCancellationRequested)
            {
                ReadResult protocolMessageResult = await _pipe.Reader.ReadAsync(runToken).ConfigureAwait(false);
                ReadOnlySequence<byte> protocolMessageBuffer = protocolMessageResult.Buffer;

                SequencePosition consumed = protocolMessageBuffer.Start;
                while (protocolMessageBuffer.Length >= 3)
                {
                    // SequenceReader handles a header split across pipe segments
                    // without allocating or stackalloc inside this long-lived loop.
                    SequenceReader<byte> reader = new(protocolMessageBuffer);
                    if (!reader.TryRead(out byte protocolIdByte) || !reader.TryReadBigEndian(out short signedPayloadLength))
                    {
                        break;
                    }

                    ushort payloadLength = unchecked((ushort)signedPayloadLength);
                    if (reader.Remaining < payloadLength)
                    {
                        break;
                    }

                    ReadOnlySequence<byte> payloadSlice = protocolMessageBuffer.Slice(3, payloadLength);
                    MuxSegmentHeader segmentHeader = new(
                        TransmissionTime: CalculateTransmissionTime(),
                        ProtocolId: (ProtocolType)protocolIdByte,
                        PayloadLength: payloadLength,
                        Mode: _muxerMode == ProtocolMode.Responder
                    );

                    MuxSegment segment = new(segmentHeader, payloadSlice);
                    await WriteSegmentAsync(segment, runToken).ConfigureAwait(false);
                    protocolMessageBuffer = protocolMessageBuffer.Slice(payloadSlice.End);
                    consumed = protocolMessageBuffer.Start;
                }

                // Retain an incomplete header/payload, and wait for more bytes
                // rather than repeatedly reading the same already-examined tail.
                _pipe.Reader.AdvanceTo(consumed, protocolMessageBuffer.End);
                if (protocolMessageResult.IsCompleted)
                {
                    if (!protocolMessageBuffer.IsEmpty)
                    {
                        throw new EndOfStreamException("Muxer input ended with an incomplete protocol message.");
                    }
                    break;
                }
            }
            runToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException ex) when (runToken.IsCancellationRequested)
        {
            muxerException = ex;
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
            Exception? completionError;
            lock (_lifecycleLock)
            {
                completionError = state.Stop(muxerException);
            }

            try
            {
                // The consumer must end first: writer completion alone cannot
                // settle a backpressured flush holding the serialization permit.
                await _pipe.Reader.CompleteAsync(completionError).ConfigureAwait(false);
                await state.WriteLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await _pipe.Writer.CompleteAsync(completionError).ConfigureAwait(false);
                }
                finally
                {
                    _ = state.WriteLock.Release();
                }
            }
            finally
            {
                Task cancellationFinished;
                lock (_lifecycleLock)
                {
                    _shutdownCompleted = true;
                    cancellationFinished = _shutdownTask;
                }
                try
                {
                    await cancellationFinished.ConfigureAwait(false);
                }
                finally
                {
                    _shutdownCancellation.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Disposes the resources used by the muxer.
    /// </summary>
    public void Dispose()
    {
        WriterState state = State;
        Exception? completionError;
        Task cancellationFinished;
        bool completeHere;
        lock (_lifecycleLock)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (_shutdownCompleted)
            {
                return;
            }

            completionError = state.Stop(new ObjectDisposedException(nameof(Muxer)));
            // Cancellation callbacks run outside this lock. A running consumer
            // owns completion, so Dispose cannot reclaim its borrowed buffer.
            _shutdownTask = _shutdownCancellation.CancelAsync();
            cancellationFinished = _shutdownTask;
            completeHere = !_runStarted;
            if (completeHere)
            {
                _shutdownCompleted = true;
            }
        }

        if (completeHere)
        {
            try
            {
                _pipe.Reader.Complete(completionError);
                state.WriteLock.Wait();
                try
                {
                    _pipe.Writer.Complete(completionError);
                }
                finally
                {
                    _ = state.WriteLock.Release();
                }
            }
            finally
            {
                try
                {
                    cancellationFinished.GetAwaiter().GetResult();
                }
                finally
                {
                    _shutdownCancellation.Dispose();
                }
            }
        }
    }
}
