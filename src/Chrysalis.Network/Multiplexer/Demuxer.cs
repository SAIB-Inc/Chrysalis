using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>Routes one owned item per mux segment to a bounded protocol queue.</summary>
/// <remarks>
/// Gate guards the queues and role: idle/stopped, owned, pending callback, queued callback, ended.
/// Start queues a callback; a pending read transfers the role to its callback. A full queue leaves
/// its segment in the bearer, and its next dequeue takes the role before returning. Every turn
/// yields after 256 KiB plus its current segment. No lock spans a bearer call or continuation.
/// Only a callback's final act may resume one reader inline, after releasing its buffer and
/// registering the next read (or stopping/requeueing). Caller turns complete all waits via the pool.
/// End cancels a pending read, never completes the bearer reader, and releases the role before disposal.
/// Role transitions under Gate (the named constants preserve the original integer representation):
/// Idle (0): Run queues a callback; a stopped-queue dequeue takes Held; End takes Ended.
/// Held (1): a turn takes PendingCallback, Idle at a full queue, QueuedCallback at budget, or Ended.
/// PendingCallback (2): completion takes Held; End cancels the read and its callback releases to Ended.
/// QueuedCallback (3): the pool callback takes Held; End can take Ended before it starts.
/// Ended (4): terminal; no callback can take the role.
/// Registration is Idle (0), Registering (1), or CompletedDuringRegistration (2): an inline
/// registration completion changes 1 to 2; the registering tail clears it and queues the callback.
/// </remarks>
public sealed class Demuxer : IDisposable
{
    private const int Idle = 0;
    private const int Held = 1;
    private const int PendingCallback = 2;
    private const int QueuedCallback = 3;
    private const int Ended = 4;
    private const int RegistrationIdle = 0;
    private const int Registering = 1;
    private const int CompletedDuringRegistration = 2;
    private const int InputBudget = 256 * 1024;
    private readonly IBearer _bearer;
    private readonly Connection _connection;
    private readonly Dictionary<ProtocolType, ProtocolQueue> _protocolQueues = [];
    private readonly TaskCompletionSource _run = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _readCallback;
    private ProtocolQueue? _stopped;
    private ConfiguredValueTaskAwaitable<ReadResult>.ConfiguredValueTaskAwaiter _pending;
    private CancellationTokenRegistration _runRegistration;
    private CancellationToken _runToken;
    private int _role;
    private int _readerThread;
    private int _registrationState;
    private bool _hasPending;
    private bool _started;

    /// <summary>Creates a demuxer that does not own or dispose its bearer.</summary>
    public Demuxer(IBearer bearer) : this(bearer, new Connection(bearer)) { }

    internal Demuxer(IBearer bearer, Connection connection)
    {
        _bearer = bearer;
        _connection = connection;
        _connection.EndRequested = EndRequested;
        _readCallback = ReadCompleted;
    }

    internal Lock Gate => _connection.Gate;
    internal bool ReaderRoleHeld { get { lock (Gate) { return _role == Held && _readerThread == Environment.CurrentManagedThreadId; } } }
    internal bool Stopped { get { lock (Gate) { return _stopped is not null && _role == Idle; } } }

    /// <summary>
    /// Subscribes to one protocol. Each segment contributes one item containing the whole messages
    /// it completed, or empty memory. At 64 queued items, all protocols stall until one is consumed.
    /// </summary>
    public ChannelReader<ReadOnlyMemory<byte>> Subscribe(ProtocolType protocol)
    {
        ProtocolQueue queue;
        ProtocolQueue.Delivery delivery = default;
        lock (Gate)
        {
            if (_protocolQueues.TryGetValue(protocol, out queue!)) { return queue; }
            queue = new ProtocolQueue(this, ProtocolLimits.Get(protocol));
            _protocolQueues.Add(protocol, queue);
            if (_connection.Ended) { delivery = queue.EndLocked(_connection.Cause); }
        }
        delivery.Complete(false);
        return queue;
    }

    /// <summary>Starts reading on the pool. A second invocation is invalid.</summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        bool start;
        lock (Gate)
        {
            if (_started) { throw new InvalidOperationException("The demuxer has already started."); }
            _started = true;
            _runToken = cancellationToken;
            start = !_connection.Ended;
            if (start) { _role = QueuedCallback; }
        }
        _runRegistration = cancellationToken.UnsafeRegister(static state =>
        {
            Demuxer demuxer = (Demuxer)state!;
            demuxer._connection.TryEnd(new OperationCanceledException(demuxer._runToken));
        }, this);
        if (start) { QueueCallback(); }
        else { FinishRun(); }
        return _run.Task;
    }

    private void QueueCallback() => ThreadPool.UnsafeQueueUserWorkItem(static demuxer => demuxer.Callback(), this, false);

    private void ReadCompleted()
    {
        // Registration can invoke us on the registering stack. Its tail queues exactly once;
        // never recurse into a turn or let a queued callback race an unfinished registration.
        if (Interlocked.CompareExchange(ref _registrationState, CompletedDuringRegistration, Registering) == Registering) { return; }
        Callback();
    }

    private void Callback()
    {
        lock (Gate)
        {
            if (_role == Ended) { return; }
            _role = Held;
            _readerThread = Environment.CurrentManagedThreadId;
        }
        Turn(true);
    }

    internal bool RestartLocked(ProtocolQueue queue)
    {
        if (_role != Idle || _stopped != queue || _connection.Ended) { return false; }
        _stopped = null;
        _role = Held;
        _readerThread = Environment.CurrentManagedThreadId;
        return true;
    }

    internal void CallerTurn() => Turn(false);

    private void Turn(bool callback)
    {
        ProtocolQueue.Delivery inline = default;
        int input = 0;
        Span<byte> header = stackalloc byte[8];
        try
        {
            while (true)
            {
                if (_connection.Ended && !_hasPending) { ReleaseEnded(); break; }
                ReadResult result;
                if (_hasPending)
                {
                    _hasPending = false;
                    result = _pending.GetResult();
                }
                else
                {
                    ValueTask<ReadResult> read = _bearer.Reader.ReadAsync();
                    if (!read.IsCompleted)
                    {
                        _pending = read.ConfigureAwait(false).GetAwaiter();
                        _hasPending = true;
                        bool cancel;
                        lock (Gate) { _role = PendingCallback; cancel = _connection.Ended; }
                        Volatile.Write(ref _registrationState, Registering);
                        if (cancel) { _bearer.Reader.CancelPendingRead(); }
                        _pending.UnsafeOnCompleted(_readCallback);
                        if (Interlocked.Exchange(ref _registrationState, RegistrationIdle) == CompletedDuringRegistration) { QueueCallback(); }
                        break;
                    }
                    result = read.GetAwaiter().GetResult();
                }
                ReadOnlySequence<byte> remaining = result.Buffer;
                SequencePosition consumed = remaining.Start;
                bool advanced = false;
                bool relinquished = false;
                try
                {
                    if (_connection.Ended) { _bearer.Reader.AdvanceTo(consumed, consumed); advanced = true; ReleaseEnded(); break; }
                    while (remaining.Length >= 8)
                    {
                        remaining.Slice(0, 8).CopyTo(header);
                        MuxSegmentCodec.ReadHeader(header, out _, out ProtocolType protocol, out ushort length, out _);
                        int size = 8 + length;
                        if (remaining.Length < size) { break; }
                        ProtocolQueue? target;
                        bool full;
                        lock (Gate)
                        {
                            _ = _protocolQueues.TryGetValue(protocol, out target);
                            full = target?.Occupancy == 64;
                        }
                        if (full)
                        {
                            // Settle the bearer BEFORE publishing the stopped role. A dequeue
                            // racing this Advance cannot restart against a held read result.
                            _bearer.Reader.AdvanceTo(consumed, consumed);
                            advanced = true;
                            lock (Gate)
                            {
                                if (!_connection.Ended && target!.Occupancy == 64) { _stopped = target; _role = Idle; relinquished = true; }
                            }
                            break;
                        }
                        if (target is not null)
                        {
                            ReadOnlyMemory<byte> item = target.Assembler.Append(remaining.Slice(8, length));
                            ProtocolQueue.Delivery delivery;
                            lock (Gate) { delivery = _connection.Ended ? default : target.EnqueueLocked(item); }
                            if (callback && inline.Queue is null) { inline = delivery; }
                            else { delivery.Complete(false); }
                        }
                        input += size;
                        remaining = remaining.Slice(size);
                        consumed = remaining.Start;
                        if (input >= InputBudget || _connection.Ended)
                        {
                            relinquished = true;
                            _bearer.Reader.AdvanceTo(consumed, consumed);
                            advanced = true;
                            if (_connection.Ended) { ReleaseEnded(); }
                            else
                            {
                                lock (Gate) { _role = QueuedCallback; }
                                QueueCallback();
                            }
                            break;
                        }
                    }
                    if (advanced)
                    {
                        if (relinquished) { break; }
                        continue; // Space appeared before the stop was published.
                    }
                    _bearer.Reader.AdvanceTo(consumed, remaining.End);
                    advanced = true;
                    if (result.IsCompleted)
                    {
                        bool partial;
                        lock (Gate) { partial = _protocolQueues.Values.Any(queue => queue.Assembler.HasPartial); }
                        _connection.End(!remaining.IsEmpty || partial ? new EndOfStreamException("Connection ended inside a segment or message.") : null);
                        ReleaseEnded();
                        break;
                    }
                }
                finally { if (!advanced) { _bearer.Reader.AdvanceTo(consumed, consumed); } }
            }
        }
        catch (Exception error)
        {
            try { _connection.End(error); }
            finally { ReleaseEnded(); }
        }
        // This is the only inline completion. No bearer result, role, or lock remains here.
        inline.Complete(true);
    }

    private void EndRequested()
    {
        List<ProtocolQueue.Delivery> deliveries = [];
        bool pending;
        bool finish;
        lock (Gate)
        {
            foreach (ProtocolQueue queue in _protocolQueues.Values) { deliveries.Add(queue.EndLocked(_connection.Cause)); }
            pending = _role == PendingCallback;
            finish = _role is Idle or QueuedCallback;
            if (finish) { _role = Ended; _stopped = null; }
        }
        foreach (ProtocolQueue.Delivery delivery in deliveries) { delivery.Complete(false); }
        if (pending) { _bearer.Reader.CancelPendingRead(); }
        if (finish) { FinishRun(); }
    }

    private void ReleaseEnded()
    {
        lock (Gate) { _role = Ended; _stopped = null; }
        FinishRun();
    }

    private void FinishRun()
    {
        lock (Gate) { foreach (ProtocolQueue queue in _protocolQueues.Values) { queue.Assembler.Dispose(); } }
        _ = _runRegistration.Unregister();
        _ = _connection.ReaderReleased.TrySetResult();
        Exception? cause = _connection.Cause;
        if (cause is OperationCanceledException && _runToken.IsCancellationRequested) { _ = _run.TrySetCanceled(_runToken); }
        else if (cause is null or ObjectDisposedException) { _ = _run.TrySetResult(); }
        else { _ = _run.TrySetException(cause); }
    }

    /// <summary>Ends reading without disposing the bearer.</summary>
    public void Dispose() => _connection.End(new ObjectDisposedException(nameof(Demuxer)));
}
