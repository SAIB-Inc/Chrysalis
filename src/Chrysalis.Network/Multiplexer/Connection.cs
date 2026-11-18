using System.IO.Pipelines;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>The first end, reader release, and serialized bearer disposal. Never completes Reader.</summary>
internal sealed class Connection(IBearer? bearer, SemaphoreSlim? writeLock = null)
{
    internal Lock Gate { get; } = new();
    internal SemaphoreSlim WriteLock { get; } = writeLock ?? new(1, 1);
    private readonly long _startTime = Environment.TickCount64;
    internal uint TransmissionTime => (uint)Math.Min(Environment.TickCount64 - _startTime, uint.MaxValue);

    internal TaskCompletionSource ReaderReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Action? EndRequested { get; set; }
    private int _ended;
    internal bool Ended => Volatile.Read(ref _ended) != 0;
    internal Exception? Cause { get; private set; }
    private int _dispose;
    private readonly TaskCompletionSource _endSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static bool IsLocal(Exception? cause) => cause is ObjectDisposedException or OperationCanceledException;

    internal void End(Exception? cause)
    {
        lock (Gate)
        {
            if (Ended) { return; }
            Cause = cause;
            Volatile.Write(ref _ended, 1);
        }
        // No bearer call or continuation under Gate. CancelPendingFlush also cancels the next
        // flush, covering a writer between its connection check and its first flush.
        try
        {
            try { bearer?.Writer.CancelPendingFlush(); }
            catch (ObjectDisposedException) { /* A completed stream writer has no flush left to cancel. */ }
            finally { EndRequested?.Invoke(); }
        }
        finally { _ = _endSettled.TrySetResult(); }
    }

    internal void ThrowIfEnded()
    {
        if (Ended) { throw new ConnectionClosedException(Cause); }
    }

    internal void CheckFlush(FlushResult result)
    {
        ThrowIfEnded();
        if (result.IsCanceled || result.IsCompleted)
        {
            throw FailSend(new IOException(result.IsCanceled ? "The bearer flush was canceled." : "The bearer writer completed."));
        }
    }

    internal ConnectionClosedException FailSend(Exception cause)
    {
        try { End(cause); }
        catch (Exception)
        {
            // End published the cause before cleanup and still notifies the reader and settles the end.
            // A secondary cleanup error must not replace the published cause at the send boundary.
        }
        return new ConnectionClosedException(Cause);
    }

    internal void DisposeBearer()
    {
        if (Interlocked.Exchange(ref _dispose, 1) != 0) { return; }
        End(new ObjectDisposedException(nameof(Plexer)));
        _ = DisposeBearerAsync();
    }

    private async Task DisposeBearerAsync()
    {
        // A concurrent end may still be canceling a bearer operation after the reader exits.
        // Its transport calls must finish too before the bearer can be disposed.
        await _endSettled.Task.ConfigureAwait(false);
        await ReaderReleased.Task.ConfigureAwait(false);
        await WriteLock.WaitAsync().ConfigureAwait(false);
        try { bearer?.Dispose(); }
        finally { _ = WriteLock.Release(); }
    }
}
