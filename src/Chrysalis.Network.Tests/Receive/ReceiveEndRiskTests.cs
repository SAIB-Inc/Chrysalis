using System.Buffers;
using System.IO.Pipelines;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

public sealed class ReceiveEndRiskTests
{
    [Theory]
    [InlineData("mux", "eof")]
    [InlineData("mux", "fault")]
    [InlineData("mux", "dispose")]
    [InlineData("raw", "eof")]
    [InlineData("raw", "fault")]
    [InlineData("raw", "dispose")]
    [InlineData("batch", "eof")]
    [InlineData("batch", "fault")]
    [InlineData("batch", "dispose")]
    public async Task S17EveryEndReleasesEveryWriterWithoutClosingTheStreamEarly(string route, string end)
    {
        using StreamBearer bearer = new();
        using Plexer plexer = new(bearer);
        AgentChannel channel = plexer.SubscribeClient(ProtocolType.BlockFetch);
        Task run = plexer.RunAsync(CancellationToken.None);
        Task send = route switch
        {
            "mux" => channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(ReceiveVectors.Body(65500))),
            "raw" => channel.WriteRawSegmentAsync(new byte[] { 1 }),
            _ => channel.WriteBatchRawSegmentsAsync(new byte[] { 1 }, 2)
        };
        try
        {
            await bearer.Stall.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (route != "mux") { Assert.False(send.IsCompleted); }
            if (end == "dispose") { plexer.Dispose(); }
            else { bearer.Input.End(end == "fault"); }
            await bearer.Stall.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(bearer.Stall.InFlush);
            Assert.Equal(0, bearer.Disposals);
            Assert.Equal(0, bearer.Input.Disposals);
            // Dispose must still not close the StreamPipeReader's underlying stream while
            // a sender owns the bearer lock, even after a peer EOF or read fault.
            plexer.Dispose();
            Assert.Equal(0, bearer.Input.Disposals);
            _ = bearer.Stall.Release.TrySetResult();
            Exception? sendError = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
            if (route != "mux") { _ = Assert.IsType<ConnectionClosedException>(sendError); }
            _ = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            await bearer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, bearer.Disposals);
            Assert.Equal(1, bearer.Input.Disposals);
            Assert.False(bearer.Stall.InFlush);
            _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => channel.WriteRawSegmentAsync(new byte[] { 1 }));
            plexer.Dispose();
            Assert.Equal(1, bearer.Disposals);
        }
        finally
        {
            _ = bearer.Stall.Release.TrySetResult();
            plexer.Dispose();
            _ = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
            _ = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task S17DisposeWaitsForTheEndToFinishItsBearerCalls()
    {
        using StreamBearer bearer = new();
        Connection connection = new(bearer);
        using Demuxer demuxer = new(bearer, connection);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bearer.Stall.OnCancel = () => { entered.SetResult(); Block(release.Task); };
        Task run = demuxer.RunAsync(CancellationToken.None);
        Task end = Task.Run(() => connection.End(new ObjectDisposedException("scripted end")));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bearer.Input.End(false);
            await connection.ReaderReleased.Task.WaitAsync(TimeSpan.FromSeconds(10));
            connection.DisposeBearer();
            Assert.Equal(0, bearer.Disposals);
            Assert.Equal(0, bearer.Input.Disposals);
        }
        finally
        {
            release.SetResult();
            await end.WaitAsync(TimeSpan.FromSeconds(10));
            connection.DisposeBearer();
            await bearer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public Task S17CompletedStreamWriterDoesNotEscapeDispose() => CompletedWriterEndAsync(dispose: true);

    [Fact]
    public Task S17CompletedStreamWriterDoesNotCrashAReadFault() => CompletedWriterEndAsync(dispose: false);

    private static async Task CompletedWriterEndAsync(bool dispose)
    {
        using StreamBearer bearer = new(completedWriter: true);
        _ = await bearer.Writer.WriteAsync(new byte[] { 0 });
        await bearer.Writer.CompleteAsync();
        Connection connection = new(bearer);
        using Demuxer demuxer = new(bearer, connection);
        Task run = demuxer.RunAsync(CancellationToken.None);
        await bearer.Input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (dispose) { demuxer.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        else
        {
            bearer.Input.End(true);
            _ = await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        await connection.ReaderReleased.Task.WaitAsync(TimeSpan.FromSeconds(10));
        connection.DisposeBearer();
        await bearer.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, bearer.Disposals);
        Assert.Equal(1, bearer.Input.Disposals);
    }

    private static void Block(Task release) => Assert.True(release.Wait(TimeSpan.FromSeconds(15)));

    private sealed class StreamBearer : IBearer
    {
        internal ReadStream Input { get; } = new();
        internal StallWriter Stall { get; } = new();
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        public PipeReader Reader { get; }
        private readonly MemoryStream _output = new();
        public PipeWriter Writer { get; }
        internal StreamBearer(bool completedWriter = false)
        {
            Reader = PipeReader.Create(Input);
            Writer = completedWriter ? PipeWriter.Create(_output) : Stall;
        }
        public void Dispose()
        {
            if (Disposals != 0) { return; }
            Assert.False(Stall.InFlush);
            Disposals++;
            Reader.Complete();
            Writer.Complete();
            Input.Dispose();
            _output.Dispose();
            Closed.SetResult();
        }
    }

    private sealed class StallWriter : PipeWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        private readonly TaskCompletionSource<FlushResult> _flush = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool InFlush { get; private set; }
        internal Action? OnCancel { get; set; }
        public override void Advance(int bytes) => _buffer.Advance(bytes);
        public override Memory<byte> GetMemory(int sizeHint = 0) => _buffer.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => _buffer.GetSpan(sizeHint);
        public override void Complete(Exception? exception = null) => Assert.False(InFlush);
        public override void CancelPendingFlush()
        {
            OnCancel?.Invoke();
            _ = Canceled.TrySetResult();
            _ = _flush.TrySetResult(new FlushResult(true, false));
        }
        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            InFlush = true;
            _ = Entered.TrySetResult();
            using CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(static state => ((StallWriter)state!).CancelPendingFlush(), this);
            try { return await _flush.Task.ConfigureAwait(false); }
            finally { await Release.Task.ConfigureAwait(false); InFlush = false; }
        }
    }

    private sealed class ReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void End(bool fault)
        {
            if (fault) { _ = _read.TrySetException(new IOException("scripted read fault")); }
            else { _ = _read.TrySetResult(0); }
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _ = Entered.TrySetResult();
            return new(_read.Task.WaitAsync(cancellationToken));
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && Disposals == 0) { Disposals++; }
            base.Dispose(disposing);
        }
    }
}
