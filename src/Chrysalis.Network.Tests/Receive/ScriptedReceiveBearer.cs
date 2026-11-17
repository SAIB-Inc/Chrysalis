using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks.Sources;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Tests.Receive;

// Every Deliver is one available read boundary. A later read can complete either immediately
// (the boundary was delivered first) or through the pool (the read was registered first).
internal sealed class ScriptedReceiveBearer : PipeReader, IBearer
{
    private readonly Lock _gate = new();
    private readonly Pipe _outgoing = new();
    private readonly List<long> _boundaries = [];
    private readonly Dictionary<int, long> _threadBytes = [];
    private byte[] _bytes = [];
    private ReadOnlySequence<byte> _held;
    private long _heldStart;
    private long _consumed;
    private long _examined;
    private TaskCompletionSource<ReadResult>? _pending;
    private RegistrationRead? _registrationRead;
    private byte[]? _registrationBytes;
    private CancellationTokenRegistration _registration;
    private bool _completed;
    private bool _disposed;
    private bool _hasResult;
    private int _reads;
    private int _synchronousReads;
    private int _endReads;

    public PipeReader Reader => this;
    public PipeWriter Writer => _outgoing.Writer;
    internal long Consumed { get { lock (_gate) { return _consumed; } } }
    internal bool Pending { get { lock (_gate) { return _pending is not null || _registrationRead is not null; } } }
    internal bool HoldingResult { get { lock (_gate) { return _hasResult; } } }
    internal Action<long>? Advanced { get; set; }
    internal int CompletionCalls { get; private set; }
    internal int CancelPendingReadCalls { get; private set; }
    internal int CallerTokenCancellations { get; private set; }
    internal int Reads => Volatile.Read(ref _reads);
    internal int SynchronousReads => Volatile.Read(ref _synchronousReads);
    internal int RegistrationCompletions { get; private set; }

    internal void CompleteNextDuringRegistration(byte[] bytes)
    {
        lock (_gate) { _registrationBytes = bytes; }
    }
    internal bool Spinning => Volatile.Read(ref _endReads) >= 32;

    internal void Deliver(ReadOnlySpan<byte> bytes, bool end = false)
    {
        TaskCompletionSource<ReadResult>? pending;
        ReadResult result = default;
        lock (_gate)
        {
            int oldLength = _bytes.Length;
            byte[] joined = new byte[oldLength + bytes.Length];
            _bytes.CopyTo(joined, 0);
            bytes.CopyTo(joined.AsSpan(oldLength));
            _bytes = joined;
            if (!bytes.IsEmpty) { _boundaries.Add(_bytes.Length); }
            _completed |= end;
            pending = _pending;
            if (pending is not null)
            {
                _pending = null;
                result = Result();
            }
        }
        if (pending is not null)
        {
            _ = _registration.Unregister();
            _ = pending.TrySetResult(result);
        }
    }

    internal void Fail(Exception error)
    {
        TaskCompletionSource<ReadResult>? pending;
        lock (_gate) { pending = _pending; _pending = null; }
        if (pending is null) { throw new InvalidOperationException("Fault requires a pending read."); }
        _ = pending.TrySetException(error);
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<ReadResult> pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hasResult || _pending is not null || _registrationRead is not null) { throw new InvalidOperationException("Two bearer reads are held"); }
            _ = Interlocked.Increment(ref _reads);
            if (_completed || _boundaries.Any(boundary => boundary > _examined))
            {
                _ = Interlocked.Increment(ref _synchronousReads);
                return ValueTask.FromResult(Result());
            }
            if (_registrationBytes is byte[] bytes)
            {
                _registrationBytes = null;
                _registrationRead = new RegistrationRead(this, bytes);
                return new ValueTask<ReadResult>(_registrationRead, 0);
            }
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = pending;
        }
        _registration = cancellationToken.UnsafeRegister(_ => Cancel(pending, cancellationToken), null);
        return new ValueTask<ReadResult>(pending.Task);
    }

    public override bool TryRead(out ReadResult result)
    {
        lock (_gate)
        {
            if (!_hasResult && _pending is null && (_completed || _boundaries.Any(boundary => boundary > _examined)))
            {
                result = Result();
                return true;
            }
            result = default;
            return false;
        }
    }

    private ReadResult Result()
    {
        long end = _boundaries.FirstOrDefault(boundary => boundary > _examined, _bytes.Length);
        _heldStart = _consumed;
        _held = new ReadOnlySequence<byte>(_bytes.AsMemory((int)_consumed, (int)(end - _consumed)));
        _hasResult = true;
        bool completed = _completed && end == _bytes.Length;
        if (completed && _examined == end && _consumed < end) { _ = Interlocked.Increment(ref _endReads); }
        return new ReadResult(_held, false, completed);
    }

    internal long ThreadBytes(int thread) { lock (_gate) { return _threadBytes.GetValueOrDefault(thread); } }

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        lock (_gate)
        {
            if (!_hasResult) { throw new InvalidOperationException("Advance without a read result"); }
            long next = _heldStart + _held.Slice(0, consumed).Length;
            int thread = Environment.CurrentManagedThreadId;
            _threadBytes[thread] = _threadBytes.GetValueOrDefault(thread) + next - _consumed;
            _consumed = next;
            _examined = _heldStart + _held.Slice(0, examined).Length;
            _hasResult = false;
        }
        Advanced?.Invoke(Consumed);
    }

    public override void CancelPendingRead()
    {
        CancelPendingReadCalls++;
        TaskCompletionSource<ReadResult>? pending;
        lock (_gate) { pending = _pending; _pending = null; }
        if (pending is not null)
        {
            ReadResult result;
            lock (_gate) { result = Result(); }
            _ = pending.TrySetResult(new ReadResult(result.Buffer, true, result.IsCompleted));
        }
    }

    private void Cancel(TaskCompletionSource<ReadResult> pending, CancellationToken token)
    {
        CallerTokenCancellations++;
        lock (_gate) { if (_pending == pending) { _pending = null; } }
        _ = pending.TrySetCanceled(token);
    }

    public override void Complete(Exception? exception = null)
    {
        CompletionCalls++;
        TaskCompletionSource<ReadResult>? pending;
        lock (_gate) { _hasResult = false; pending = _pending; _pending = null; }
        if (pending is not null)
        {
            if (exception is null) { _ = pending.TrySetResult(new ReadResult(default, false, true)); }
            else { _ = pending.TrySetException(exception); }
        }
    }

    // This deliberately completes on the awaiter's registering stack, not a producer's stack.
    // It forces the registration race which the shared reader must trampoline before resuming
    // user code. A precompleted ValueTask alone does not exercise that race.
    private sealed class RegistrationRead(ScriptedReceiveBearer owner, byte[] bytes) : IValueTaskSource<ReadResult>
    {
        private ReadResult _result;
        private bool _completed;
        public ReadResult GetResult(short token) => _completed ? _result : throw new InvalidOperationException("Read is not complete");
        public ValueTaskSourceStatus GetStatus(short token) => _completed ? ValueTaskSourceStatus.Succeeded : ValueTaskSourceStatus.Pending;
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            owner.Deliver(bytes);
            lock (owner._gate)
            {
                AssertRegistration();
                owner._registrationRead = null;
                _result = owner.Result();
                _completed = true;
                owner.RegistrationCompletions++;
            }
            continuation(state);
        }
        private void AssertRegistration()
        {
            if (owner._registrationRead != this || _completed) { throw new InvalidOperationException("Read registered twice"); }
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) { return; } _disposed = true; }
        Complete(new ObjectDisposedException(nameof(ScriptedReceiveBearer)));
        _registration.Dispose();
        _outgoing.Reader.Complete();
        _outgoing.Writer.Complete();
    }
}
