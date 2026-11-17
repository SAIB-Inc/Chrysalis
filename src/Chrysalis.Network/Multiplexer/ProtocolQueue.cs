using System.Threading.Channels;
using System.Threading.Tasks.Sources;

namespace Chrysalis.Network.Multiplexer;

/// <summary>A bounded single-consumer segment queue. Only consuming an item frees a slot.</summary>
internal sealed class ProtocolQueue(Demuxer owner, int? limit) : ChannelReader<ReadOnlyMemory<byte>>,
    IValueTaskSource<ReadOnlyMemory<byte>>, IValueTaskSource<bool>
{
    // A pending operation is either a read or a notification, never both.
    private const int NoWaiter = 0;
    private const int ReadWaiter = 1;
    private const int NotificationWaiter = 2;
    private readonly Demuxer _owner = owner;
    private readonly ReadOnlyMemory<byte>[] _items = new ReadOnlyMemory<byte>[64];
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal MessageAssembler Assembler { get; } = new(limit);
    private ManualResetValueTaskSourceCore<ReadOnlyMemory<byte>> _read;
    private ManualResetValueTaskSourceCore<bool> _wait;
    private CancellationTokenRegistration _registration;
    private int _head;
    private int _waiting;
    private int _outstanding;
    private bool _ended;
    private Exception? _cause;
    private int _generation;
    internal bool Waiting { get { lock (_owner.Gate) { return _waiting != NoWaiter; } } }
    internal int Occupancy { get; private set; } // Caller holds Gate.
    public override bool CanCount => true;
    public override bool CanPeek => true;
    public override Task Completion => _completion.Task;
    public override int Count { get { lock (_owner.Gate) { return Occupancy; } } }

    public override bool TryPeek(out ReadOnlyMemory<byte> item)
    {
        lock (_owner.Gate) { item = Occupancy == 0 ? default : _items[_head]; return Occupancy != 0; }
    }

    public override bool TryRead(out ReadOnlyMemory<byte> item)
    {
        bool restart;
        lock (_owner.Gate)
        {
            if (Occupancy == 0) { item = default; return false; }
            item = _items[_head];
            _items[_head] = default;
            _head = (_head + 1) % 64;
            Occupancy--;
            Drained();
            restart = _owner.RestartLocked(this);
        }
        if (restart) { _owner.CallerTurn(); }
        return true;
    }

    public override ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) { return ValueTask.FromCanceled<ReadOnlyMemory<byte>>(cancellationToken); }
        lock (_owner.Gate) { RejectConcurrent(); }
        if (TryRead(out ReadOnlyMemory<byte> item)) { return ValueTask.FromResult(item); }
        int generation;
        short version;
        bool retry;
        lock (_owner.Gate)
        {
            RejectConcurrent();
            // A producer may have queued an item since TryRead. Retry outside the gate so a
            // stopped reader's turn never runs under this lock. Every generation value is valid.
            retry = Occupancy != 0;
            if (retry) { generation = 0; version = 0; }
            else
            {
                if (_ended) { return ValueTask.FromException<ReadOnlyMemory<byte>>(new ConnectionClosedException(_cause)); }
                _read.Reset();
                _read.RunContinuationsAsynchronously = true;
                _waiting = _outstanding = ReadWaiter;
                generation = ++_generation;
                version = _read.Version;
            }
        }
        if (retry) { return ReadAsync(cancellationToken); }
        Register(generation, cancellationToken);
        return new ValueTask<ReadOnlyMemory<byte>>(this, version);
    }

    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) { return ValueTask.FromCanceled<bool>(cancellationToken); }
        int generation;
        short version;
        lock (_owner.Gate)
        {
            RejectConcurrent();
            if (Occupancy != 0) { return ValueTask.FromResult(true); }
            if (_ended) { return WaitEnd(); }
            _wait.Reset();
            _wait.RunContinuationsAsynchronously = true;
            _waiting = _outstanding = NotificationWaiter;
            generation = ++_generation;
            version = _wait.Version;
        }
        Register(generation, cancellationToken);
        return new ValueTask<bool>(this, version);
    }

    private ValueTask<bool> WaitEnd() => _cause is null || Connection.IsLocal(_cause)
        ? ValueTask.FromResult(false) : ValueTask.FromException<bool>(_cause);

    private void RejectConcurrent()
    {
        if (_outstanding != NoWaiter) { throw new InvalidOperationException("Only one protocol read may be outstanding."); }
    }

    private void Register(int generation, CancellationToken token)
    {
        if (!token.CanBeCanceled) { return; }
        CancellationTokenRegistration registration = token.UnsafeRegister(static state =>
        {
            (ProtocolQueue queue, int version, CancellationToken cancellation) = ((ProtocolQueue, int, CancellationToken))state!;
            Delivery delivery;
            lock (queue._owner.Gate)
            {
                if (queue._waiting == NoWaiter || queue._generation != version) { return; }
                delivery = queue.TakeWaiter(default, new OperationCanceledException(cancellation));
            }
            delivery.Complete(false);
        }, (this, generation, token));
        bool keep;
        lock (_owner.Gate)
        {
            keep = _waiting != NoWaiter && _generation == generation;
            if (keep) { _registration = registration; }
        }
        if (!keep) { _ = registration.Unregister(); }
    }

    internal Delivery EnqueueLocked(ReadOnlyMemory<byte> item)
    {
        if (_waiting == ReadWaiter) { return TakeWaiter(item, null); }
        _items[(_head + Occupancy++) % 64] = item;
        return _waiting == NotificationWaiter ? TakeWaiter(default, null) : default;
    }

    internal Delivery EndLocked(Exception? cause)
    {
        _ended = true;
        _cause = cause;
        Drained();
        return _waiting == NoWaiter ? default : TakeWaiter(default,
            _waiting == ReadWaiter ? new ConnectionClosedException(cause) : cause is null || Connection.IsLocal(cause) ? null : cause);
    }

    private void Drained()
    {
        if (!_ended || Occupancy != 0) { return; }
        if (_cause is null || Connection.IsLocal(_cause)) { _ = _completion.TrySetResult(); }
        else { _ = _completion.TrySetException(_cause); }
    }

    private Delivery TakeWaiter(ReadOnlyMemory<byte> item, Exception? error)
    {
        Delivery delivery = new(this, _waiting, item, error, _registration, !_ended || Occupancy != 0);
        _waiting = NoWaiter;
        _registration = default;
        return delivery;
    }

    internal readonly record struct Delivery(ProtocolQueue? Queue, int Kind, ReadOnlyMemory<byte> Item,
        Exception? Error, CancellationTokenRegistration Registration, bool Available)
    {
        internal void Complete(bool inline)
        {
            if (Queue is null) { return; }
            _ = Registration.Unregister();
            if (Kind == ReadWaiter)
            {
                Queue._read.RunContinuationsAsynchronously = !inline;
                if (Error is null) { Queue._read.SetResult(Item); }
                else { Queue._read.SetException(Error); }
            }
            else
            {
                Queue._wait.RunContinuationsAsynchronously = !inline;
                if (Error is null) { Queue._wait.SetResult(Available); }
                else { Queue._wait.SetException(Error); }
            }
        }
    }

    ReadOnlyMemory<byte> IValueTaskSource<ReadOnlyMemory<byte>>.GetResult(short token)
    {
        try { return _read.GetResult(token); }
        finally { lock (_owner.Gate) { _outstanding = NoWaiter; } }
    }
    bool IValueTaskSource<bool>.GetResult(short token)
    {
        try { return _wait.GetResult(token); }
        finally { lock (_owner.Gate) { _outstanding = NoWaiter; } }
    }
    ValueTaskSourceStatus IValueTaskSource<ReadOnlyMemory<byte>>.GetStatus(short token) => _read.GetStatus(token);
    ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _wait.GetStatus(token);
    void IValueTaskSource<ReadOnlyMemory<byte>>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _read.OnCompleted(continuation, state, token, flags);
    void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _wait.OnCompleted(continuation, state, token, flags);
}
