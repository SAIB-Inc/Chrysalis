using ReceiveOracle = Chrysalis.Network.Tests.Receive.Frozen;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Channels;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

internal sealed class ReceiveDriver : IAsyncDisposable
{
    private readonly IDisposable _demuxer;
    private readonly CancellationTokenSource _runToken = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Pipe _unused = new();
    private readonly ReceiveSchedule _schedule;
    private bool _disposed;
    private bool _spin;

    internal ReceiveDriver(ReceiveSchedule schedule, bool frozen, bool start = true)
    {
        _schedule = schedule;
        if (frozen)
        {
            ReceiveOracle.Demuxer demuxer = new(Bearer);
            _demuxer = demuxer;
            foreach ((ProtocolType protocol, ReceiveConsumer mode) in schedule.Consumers)
            {
                AddConsumer(demuxer, demuxer.Subscribe(protocol), protocol, mode, true);
            }
            Run = start ? Task.Run(() => demuxer.RunAsync(_runToken.Token)) : Task.CompletedTask;
        }
        else
        {
            Demuxer demuxer = new(Bearer);
            _demuxer = demuxer;
            foreach ((ProtocolType protocol, ReceiveConsumer mode) in schedule.Consumers)
            {
                AddConsumer(demuxer, demuxer.Subscribe(protocol), protocol, mode, false);
            }
            Run = start ? Task.Run(() => demuxer.RunAsync(_runToken.Token)) : Task.CompletedTask;
        }
    }

    internal ScriptedReceiveBearer Bearer { get; } = new();
    internal Dictionary<ProtocolType, ReceiveProbe> Probes { get; } = [];
    internal Dictionary<ProtocolType, ReceiveCalls> Consumers { get; } = [];
    internal Task Run { get; private set; }

    private void AddConsumer(object demuxer, ChannelReader<ReadOnlyMemory<byte>> reader, ProtocolType protocol, ReceiveConsumer mode, bool frozen)
    {
        ReceiveProbe probe;
        if (frozen)
        {
            FieldInfo field = demuxer.GetType().GetField("_protocolChannels", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ConcurrentDictionary<ProtocolType, Channel<ReadOnlyMemory<byte>>> channels =
                (ConcurrentDictionary<ProtocolType, Channel<ReadOnlyMemory<byte>>>)field.GetValue(demuxer)!;
            probe = new(reader, channels[protocol].Writer,
                [.. _schedule.Segments.Where(segment => segment.Protocol == protocol)],
                [.. _schedule.Messages.Where(message => message.Protocol == protocol)]);
            channels[protocol] = probe;
        }
        else
        {
            probe = new(reader, Channel.CreateUnbounded<ReadOnlyMemory<byte>>().Writer,
                [.. _schedule.Segments.Where(segment => segment.Protocol == protocol)],
                [.. _schedule.Messages.Where(message => message.Protocol == protocol)], () => ((Demuxer)demuxer).Stopped);
        }
        Probes.Add(protocol, probe);
        AgentChannel channel = new(protocol, _unused.Writer, Bearer.Writer, probe.Reader, _sendLock) { WholeMessages = !frozen };
        Consumers.Add(protocol, new ReceiveCalls(mode, probe, channel, frozen, Bearer));
    }

    internal void Start()
    {
        Assert.Same(Task.CompletedTask, Run);
        Run = _demuxer is ReceiveOracle.Demuxer frozen
            ? Task.Run(() => frozen.RunAsync(_runToken.Token))
            : Task.Run(() => ((Demuxer)_demuxer).RunAsync(_runToken.Token));
    }

    internal async Task QuietAsync()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        int yields = 0;
        while (true)
        {
            if (Bearer.Spinning)
            {
                _spin = true;
                await _runToken.CancelAsync();
                _ = await Record.ExceptionAsync(() => Run);
                Bearer.Dispose();
            }
            bool stableConsumers = Consumers.Values.All(consumer => consumer.Quiet);
            bool stableSource = Run.IsCompleted || Bearer.Pending || Probes.Values.Any(probe => probe.Stopped);
            if (stableConsumers && stableSource)
            {
                // A completed channel wait must reach the logical caller before a snapshot.
                await Task.Yield();
                if (Consumers.Values.All(consumer => consumer.Quiet) &&
                    (Run.IsCompleted || Bearer.Pending || Probes.Values.Any(probe => probe.Stopped))) { return; }
            }
            if (deadline.IsCancellationRequested)
            {
                Assert.Fail($"{_schedule.Name}: no quiet point; run={Run.Status}, pendingRead={Bearer.Pending}, held={Bearer.HoldingResult}, " +
                    string.Join(";", Probes.Select(pair => $"{pair.Key}:count={pair.Value.Reader.Count},write={pair.Value.Writing},park={pair.Value.Parked},calls={Consumers[pair.Key].Quiet}")));
            }
            if (++yields % 64 == 0) { await Task.Delay(1); }
            else { await Task.Yield(); }
        }
    }

    internal object Snapshot()
    {
        int progress = _schedule.Segments.Where(segment => segment.WireEnd <= Bearer.Consumed).Select(segment => segment.Index).DefaultIfEmpty(-1).Max();
        foreach (ReceiveProbe probe in Probes.Values)
        {
            progress = Math.Max(progress, probe.Admitted.DefaultIfEmpty(-1).Max());
            if (probe.Writing is int writing) { progress = Math.Max(progress, writing - 1); }
            Assert.Equal(probe.Admitted.Count - probe.Dequeued.Count, probe.Reader.Count);
            Assert.InRange(probe.Reader.Count, 0, 64);
            Assert.Equal(probe.Admitted.Take(probe.Dequeued.Count), probe.Dequeued);
            probe.CheckItems(_schedule, wholeMessages: _demuxer is Demuxer);
        }
        int[] admitted = [.. Probes.Values.SelectMany(probe => probe.Admitted).Order()];
        Assert.Equal(_schedule.Segments.Where(segment => segment.Index <= progress && Consumers.ContainsKey(segment.Protocol)).Select(segment => segment.Index), admitted);
        return new
        {
            Progress = progress,
            Dropped = _schedule.Segments.Where(segment => segment.Index <= progress && !Consumers.ContainsKey(segment.Protocol)).Select(segment => segment.Index).ToArray(),
            Queues = Probes.Select(pair => new
            {
                Protocol = pair.Key,
                pair.Value.Admitted,
                pair.Value.Dequeued,
                pair.Value.Reader.Count,
                Calls = Consumers[pair.Key].Snapshot()
            }).ToArray(),
            End = _spin ? "spin" : Run.IsCompleted ? _disposed ? "disposed" : Run.IsFaulted ? "fault" : Run.IsCanceled ? "cancelled" : "clean" : "running",
            Cause = _spin || _disposed ? null : Run.Exception?.InnerException?.GetType().FullName
        };
    }

    internal void Close()
    {
        _disposed = true;
        _demuxer.Dispose();
        // The driver owns the bearer, just as Plexer.Dispose does in fc21a49. A standalone
        // Demuxer.Dispose does not cancel its bearer read on today's code.
        Bearer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        await _runToken.CancelAsync();
        _ = await Record.ExceptionAsync(() => Run.WaitAsync(TimeSpan.FromSeconds(10)));
        foreach (ReceiveCalls calls in Consumers.Values) { await calls.DisposeAsync(); }
        _runToken.Dispose();
        _sendLock.Dispose();
        await _unused.Reader.CompleteAsync();
        await _unused.Writer.CompleteAsync();
    }
}

internal sealed class ReceiveProbe : Channel<ReadOnlyMemory<byte>>
{
    private readonly Lock _gate = new();
    private readonly ReceiveSegment[] _segments;
    private readonly ReceiveMessage[] _expectedMessages;
    private int _checkedItems;
    private int _checkedMessages;
    private int _normalizedItem;
    private int _normalizedOffset;
    private long _itemBytes;
    private long _messageBytes;
    private readonly ChannelReader<ReadOnlyMemory<byte>> _inner;
    private readonly ProtocolQueue? _prototype;
    private readonly Func<bool>? _stopped;
    private int _nextWrite;
    private int _nextRead;
    private bool _reading;
    private bool _released;
    private bool _ended;
    private CancellationToken _readToken;
    private CancellationToken _writeToken;
    private bool _pendingAdmitted;
    internal List<int> Admitted
    {
        get
        {
            if (_prototype is not null)
            {
                lock (_gate)
                {
                    int admitted = _nextRead + _prototype.Count;
                    while (field.Count < admitted) { field.Add(_segments[field.Count].Index); }
                }
            }
            return field;
        }
    } = [];
    internal List<int> Dequeued { get; } = [];
    internal List<byte[]> Items { get; } = [];
    internal int? Writing { get; private set; }
    internal int Parked { get { lock (_gate) { return _prototype is not null ? _prototype.Waiting ? 1 : 0 : _reading && !_released && !_ended && !_readToken.IsCancellationRequested ? 1 : 0; } } }
    internal bool Stopped { get { lock (_gate) { return _prototype is not null ? _stopped!() : Writing is not null && !_pendingAdmitted && !_ended && !_writeToken.IsCancellationRequested && _inner.Count == 64; } } }

    internal ReceiveProbe(ChannelReader<ReadOnlyMemory<byte>> reader, ChannelWriter<ReadOnlyMemory<byte>> writer, ReceiveSegment[] segments, ReceiveMessage[] messages, Func<bool>? stopped = null)
    {
        _segments = segments;
        _expectedMessages = messages;
        _inner = reader;
        _prototype = reader as ProtocolQueue;
        _stopped = stopped;
        Reader = new ProbeReader(reader, this);
        Writer = new ProbeWriter(writer, this);
    }

    internal void CheckItems(ReceiveSchedule schedule, bool wholeMessages = false)
    {
        // Each new item and each completed message is checked once. Rechecking the full byte
        // history at every step would make the required 100,000-seed run quadratic in payload.
        for (; _checkedItems < Items.Count; _checkedItems++)
        {
            byte[] item = Items[_checkedItems];
            Assert.Equal(schedule.ItemBytes(_segments[_checkedItems].Index, wholeMessages), item);
            _itemBytes += item.Length;
        }
        if (Items.Count == 0) { return; }
        while (_checkedMessages < _expectedMessages.Length && _expectedMessages[_checkedMessages].LastSegment <= Dequeued[^1])
        {
            byte[] message = _expectedMessages[_checkedMessages++].Bytes;
            int offset = 0;
            while (offset < message.Length)
            {
                while (_normalizedItem < Items.Count && _normalizedOffset == Items[_normalizedItem].Length)
                {
                    _normalizedItem++;
                    _normalizedOffset = 0;
                }
                Assert.True(_normalizedItem < Items.Count, "A completed manifest message is missing from the raw stream");
                byte[] item = Items[_normalizedItem];
                int count = Math.Min(message.Length - offset, item.Length - _normalizedOffset);
                Assert.True(message.AsSpan(offset, count).SequenceEqual(item.AsSpan(_normalizedOffset, count)), "Normalized message bytes differ from the manifest");
                offset += count;
                _normalizedOffset += count;
            }
            _messageBytes += message.Length;
        }
        if (wholeMessages) { Assert.Equal(_messageBytes, _itemBytes); }
        else { Assert.True(_messageBytes <= _itemBytes); }
    }

    private void Took(ReadOnlyMemory<byte> item, bool freedSlot = true)
    {
        Dequeued.Add(_segments[_nextRead++].Index);
        Items.Add(item.ToArray());
        // BoundedChannel transfers its waiting writer's item into the freed slot inside the
        // dequeue, before that writer's continuation runs. Observe admission at that same event.
        if (freedSlot && Writing is int pending && !_pendingAdmitted && !_ended && !_writeToken.IsCancellationRequested)
        {
            Admitted.Add(pending);
            _pendingAdmitted = true;
        }
    }

    private sealed class ProbeReader(ChannelReader<ReadOnlyMemory<byte>> inner, ReceiveProbe probe) : ChannelReader<ReadOnlyMemory<byte>>
    {
        public override bool CanCount => inner.CanCount;
        public override int Count => inner.Count;
        public override bool CanPeek => inner.CanPeek;
        public override Task Completion => inner.Completion;
        public override bool TryPeek(out ReadOnlyMemory<byte> item) => inner.TryPeek(out item);
        public override bool TryRead(out ReadOnlyMemory<byte> item)
        {
            lock (probe._gate)
            {
                if (!inner.TryRead(out item)) { return false; }
                probe.Took(item);
                return true;
            }
        }
        public override ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
        {
            lock (probe._gate)
            {
                ValueTask<ReadOnlyMemory<byte>> read = inner.ReadAsync(cancellationToken);
                if (read.IsCompletedSuccessfully)
                {
                    ReadOnlyMemory<byte> item = read.Result;
                    probe.Took(item);
                    return ValueTask.FromResult(item);
                }
                probe._reading = !read.IsCompleted;
                probe._released = false;
                probe._readToken = cancellationToken;
                return FinishReadAsync(read);
            }
        }
        private async ValueTask<ReadOnlyMemory<byte>> FinishReadAsync(ValueTask<ReadOnlyMemory<byte>> read)
        {
            try
            {
                ReadOnlyMemory<byte> item = await read.ConfigureAwait(false);
                // A parked ReadAsync was handed the item directly. Its later continuation
                // does not dequeue from the queue and must not admit a now-blocked writer.
                lock (probe._gate) { probe.Took(item, freedSlot: false); }
                return item;
            }
            finally { lock (probe._gate) { probe._reading = false; } }
        }
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            lock (probe._gate)
            {
                ValueTask<bool> wait = inner.WaitToReadAsync(cancellationToken);
                if (wait.IsCompletedSuccessfully) { return ValueTask.FromResult(wait.Result); }
                probe._reading = !wait.IsCompleted;
                probe._released = false;
                probe._readToken = cancellationToken;
                return FinishWaitAsync(wait);
            }
        }
        private async ValueTask<bool> FinishWaitAsync(ValueTask<bool> wait)
        {
            try { return await wait.ConfigureAwait(false); }
            finally { lock (probe._gate) { probe._reading = false; } }
        }
    }

    private sealed class ProbeWriter(ChannelWriter<ReadOnlyMemory<byte>> inner, ReceiveProbe probe) : ChannelWriter<ReadOnlyMemory<byte>>
    {
        public override bool TryComplete(Exception? error = null)
        {
            lock (probe._gate)
            {
                probe._ended = true;
                probe._released = true;
                return inner.TryComplete(error);
            }
        }
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => inner.WaitToWriteAsync(cancellationToken);
        public override bool TryWrite(ReadOnlyMemory<byte> item)
        {
            lock (probe._gate)
            {
                if (!inner.TryWrite(item)) { return false; }
                probe.Admitted.Add(probe._segments[probe._nextWrite++].Index);
                probe._released = true;
                return true;
            }
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> item, CancellationToken cancellationToken = default)
        {
            lock (probe._gate)
            {
                int index = probe._segments[probe._nextWrite++].Index;
                ValueTask write = inner.WriteAsync(item, cancellationToken);
                if (write.IsCompletedSuccessfully)
                {
                    probe.Admitted.Add(index);
                    probe._released = true;
                    return ConsumeCompletedAsync(write);
                }
                probe.Writing = index;
                probe._writeToken = cancellationToken;
                probe._pendingAdmitted = false;
                return FinishWriteAsync(write, index);
            }
        }
        private static async ValueTask ConsumeCompletedAsync(ValueTask write) => await write.ConfigureAwait(false);

        private async ValueTask FinishWriteAsync(ValueTask write, int index)
        {
            try
            {
                await write.ConfigureAwait(false);
                lock (probe._gate)
                {
                    // The dequeue already recorded admission. Its item may even have been
                    // drained and a newer read parked before this continuation was scheduled.
                    // A delayed writer completion must never notify that newer read.
                    Assert.True(probe._pendingAdmitted, $"Writer {index} completed without an observed admission");
                }
            }
            finally { lock (probe._gate) { probe.Writing = null; } }
        }
    }
}

internal sealed class ReceiveCalls : IAsyncDisposable
{
    private readonly ScriptedReceiveBearer _bearer;
    private readonly ReceiveConsumer _mode;
    private readonly ReceiveProbe _probe;
    private readonly Func<CancellationToken, Task<BlockFetchMessage>> _receive;
    private readonly List<Task<string>> _calls = [];
    private readonly List<byte[]> _messages = [];
    private readonly List<string> _messageHashes = [];
    private ReceiveMessage[]? _expectedMessages;
    private int _checkedMessages;
    private int _checkedCalls;
    private readonly CancellationTokenSource _lifetime = new();
    private IAsyncEnumerator<ReadOnlyMemory<byte>>? _enumerator;
    private CancellationTokenSource? _callToken;
    private bool _waitThenTry;

    internal ReceiveCalls(ReceiveConsumer mode, ReceiveProbe probe, AgentChannel channel, bool frozen, ScriptedReceiveBearer bearer)
    {
        _bearer = bearer;
        _mode = mode;
        _probe = probe;
        _receive = frozen
            ? new ReceiveOracle.ChannelBuffer(channel).ReceiveFullMessageAsync<BlockFetchMessage>
            : new ChannelBuffer(channel).ReceiveFullMessageAsync<BlockFetchMessage>;
    }

    internal bool Quiet => _calls.Count == 0 || _calls[^1].IsCompleted || _probe.Parked > 0;
    internal bool Pending => _calls.Count > 0 && !_calls[^1].IsCompleted;
    internal Task Last => _calls[^1];
    internal string? LastOutcome => _calls.Count > 0 && _calls[^1].IsCompletedSuccessfully ? _calls[^1].Result : null;

    internal void Call()
    {
        if (Pending) { return; } // One outstanding call per protocol, as the schedule requires.
        _callToken?.Dispose();
        _callToken = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _calls.Add(InvokeAsync(_callToken.Token));
    }

    internal List<long> ReturnCheckpoints { get; } = [];

    private async Task<string> InvokeAsync(CancellationToken token)
    {
        int thread = Environment.CurrentManagedThreadId;
        long before = _bearer.ThreadBytes(thread);
        try
        {
            switch (_mode)
            {
                case ReceiveConsumer.Buffer:
                    BlockFetchMessage message = await _receive(token).ConfigureAwait(false);
                    byte[] snapshot = message.Raw.ToArray(); // Snapshot before the next receive can overwrite _temp.
                    _messages.Add(snapshot);
                    _messageHashes.Add(Convert.ToHexString(SHA256.HashData(snapshot)));
                    return "message";
                case ReceiveConsumer.Read:
                    _ = await _probe.Reader.ReadAsync(token).ConfigureAwait(false);
                    return "item";
                case ReceiveConsumer.Poll:
                    return _probe.Reader.TryRead(out _) ? "item" : "empty";
                case ReceiveConsumer.Wait:
                    if (_waitThenTry)
                    {
                        _waitThenTry = false;
                        return _probe.Reader.TryRead(out _) ? "item" : "empty";
                    }
                    bool ready = await _probe.Reader.WaitToReadAsync(token).ConfigureAwait(false);
                    _waitThenTry = ready;
                    return ready ? "true" : "false";
                case ReceiveConsumer.All:
                    _enumerator ??= _probe.Reader.ReadAllAsync(_lifetime.Token).GetAsyncEnumerator(CancellationToken.None);
                    return await _enumerator.MoveNextAsync().ConfigureAwait(false) ? "item" : "false";
                default: throw new InvalidOperationException();
            }
        }
        catch (Exception exception) { return exception.GetType().FullName!; }
        finally
        {
            ReturnCheckpoints.Add(Environment.CurrentManagedThreadId == thread ? _bearer.ThreadBytes(thread) - before : -1);
        }
    }

    internal void Cancel()
    {
        if (_mode == ReceiveConsumer.All) { _lifetime.Cancel(); }
        else { _callToken?.Cancel(); }
    }

    internal object Snapshot() => new
    {
        Calls = _calls.Select(call => call.IsCompletedSuccessfully ? Canonical(call.Result) : "pending").ToArray(),
        Messages = _messageHashes.ToArray()
    };

    private string Canonical(string result)
    {
        if (result == typeof(ConnectionClosedException).FullName) { return typeof(ChannelClosedException).FullName!; }
        if (result == typeof(EndOfStreamException).FullName && _mode is ReceiveConsumer.Wait or ReceiveConsumer.All) { return "false"; }
        return result;
    }

    internal void CheckMessages(ReceiveSchedule schedule, ProtocolType protocol)
    {
        for (; _checkedCalls < _calls.Count && _calls[_checkedCalls].IsCompletedSuccessfully; _checkedCalls++)
        {
            string result = _calls[_checkedCalls].Result;
            bool allowed = result is "message" or "item" or "empty" or "true" or "false" or
                "System.Threading.Tasks.TaskCanceledException" or "System.OperationCanceledException" or
                "System.Threading.Channels.ChannelClosedException" or "Chrysalis.Network.Multiplexer.ConnectionClosedException" ||
                (result == "System.IO.EndOfStreamException" && schedule.DeclaredDifference?.StartsWith("EOF", StringComparison.Ordinal) == true) ||
                (result == "SAIB.Cbor.CborException" && schedule.DeclaredDifference?.StartsWith("Invalid CBOR", StringComparison.Ordinal) == true);
            Assert.True(allowed, $"{schedule.Name}: unexpected consumer result {result}");
        }
        _expectedMessages ??= [.. schedule.Messages.Where(message => message.Protocol == protocol)];
        Assert.True(_messages.Count <= _expectedMessages.Length);
        for (; _checkedMessages < _messages.Count; _checkedMessages++)
        {
            Assert.Equal(_expectedMessages[_checkedMessages].Bytes, _messages[_checkedMessages]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _ = await Record.ExceptionAsync(() => Task.WhenAll(_calls).WaitAsync(TimeSpan.FromSeconds(10)));
        if (_enumerator is not null) { await _enumerator.DisposeAsync(); }
        _callToken?.Dispose();
        _lifetime.Dispose();
    }
}
