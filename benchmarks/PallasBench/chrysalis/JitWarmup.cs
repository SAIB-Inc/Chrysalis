using System.Diagnostics;
using System.Runtime;

namespace Chrysalis.Benchmarks;

internal readonly record struct JitSnapshot(long CompiledMethods, long IlBytes, long CompilationTicks)
{
    internal static JitSnapshot Capture() => new(JitInfo.GetCompiledMethodCount(), JitInfo.GetCompiledILBytes(), JitInfo.GetCompilationTime().Ticks);
}

internal readonly record struct TimedWindow(long Elapsed, JitSnapshot Start, JitSnapshot End);

internal sealed class JitQuietWindow(TimeSpan minimum, TimeSpan quiet, TimeSpan cap)
{
    private long previous = -1;
    private TimeSpan changedAt;

    internal bool Observe(TimeSpan elapsed, long count)
    {
        if (elapsed >= cap) { throw new TimeoutException("JIT did not settle within the warm-up cap; no measurement."); }
        if (count != previous) { previous = count; changedAt = elapsed; }
        return elapsed >= minimum && elapsed - changedAt >= quiet;
    }
}

internal static class JitWarmup
{
    internal const int MinimumSeconds = 20;
    internal const int QuietSeconds = 10;
    internal const int CapSeconds = 120;
    internal const int MeasuredWindowSeconds = CapSeconds;
    internal const int ProcessMarginSeconds = 30;

    internal static async Task<int> WaitAsync(Func<Task> operation)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(CapSeconds));
        JitQuietWindow window = new(TimeSpan.FromSeconds(MinimumSeconds), TimeSpan.FromSeconds(QuietSeconds), TimeSpan.FromSeconds(CapSeconds));
        Stopwatch timer = Stopwatch.StartNew();
        int passes = 0;
        try
        {
            do
            {
                await operation().WaitAsync(deadline.Token).ConfigureAwait(false);
                passes++;
            }
            while (!window.Observe(timer.Elapsed, JitInfo.GetCompiledMethodCount()));
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("JIT did not settle within the warm-up cap; no measurement.");
        }
        return passes;
    }

    internal static async Task<(TimedWindow Sample, int WarmupIterations)> MeasureAfterAsync(
        Task<int> warmup, Func<CancellationToken, Task<TimedWindow>> measure, TimeProvider? clock = null)
    {
        int passes = await warmup.ConfigureAwait(false);
        using CancellationTokenSource measured = new(TimeSpan.FromSeconds(MeasuredWindowSeconds), clock ?? TimeProvider.System);
        return (await measure(measured.Token).ConfigureAwait(false), passes);
    }

    internal static async Task VerifyMeasurementLifetimeAsync()
    {
        PhaseClock clock = new();
        TaskCompletionSource<int> warmup = new();
        Task<(TimedWindow Sample, int WarmupIterations)> pending = MeasureAfterAsync(warmup.Task, token =>
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            if (token.IsCancellationRequested) { throw new InvalidDataException("Near-cap warmup stole the measured window."); }
            clock.Advance(TimeSpan.FromSeconds(MeasuredWindowSeconds - 2));
            if (!token.IsCancellationRequested) { throw new InvalidDataException("Measured-window deadline was not enforced."); }
            return Task.FromResult(default(TimedWindow));
        }, clock);
        clock.Advance(TimeSpan.FromSeconds(CapSeconds - 1));
        warmup.SetResult(1);
        _ = await pending.ConfigureAwait(false);
    }

    internal static void Verify()
    {
        JitQuietWindow window = new(TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        if (window.Observe(TimeSpan.Zero, 10) || window.Observe(TimeSpan.FromSeconds(1), 10)
            || window.Observe(TimeSpan.FromSeconds(2), 11) || window.Observe(TimeSpan.FromSeconds(3), 11)
            || !window.Observe(TimeSpan.FromSeconds(4), 11))
        {
            throw new InvalidDataException("JIT quiet-interval/reset check failed.");
        }
        try { _ = window.Observe(TimeSpan.FromSeconds(5), 12); }
        catch (TimeoutException) { return; }
        throw new InvalidDataException("Unsettled JIT was accepted beyond the cap.");
    }
}
