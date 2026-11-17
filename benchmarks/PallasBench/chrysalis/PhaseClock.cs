namespace Chrysalis.Benchmarks;

// Golden-check clock: only one-shot timers are needed by CancellationTokenSource.
internal sealed class PhaseClock : TimeProvider
{
    private TimeSpan elapsed;
    private readonly List<ClockTimer> timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ClockTimer timer = new(this, callback, state);
        _ = timer.Change(dueTime, period);
        timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan duration)
    {
        elapsed += duration;
        foreach (ClockTimer timer in timers) { timer.Fire(elapsed); }
    }

    private sealed class ClockTimer(PhaseClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan dueAt;
        private bool active;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) { throw new NotSupportedException("One-shot golden timer only."); }
            dueAt = clock.elapsed + dueTime;
            active = dueTime != Timeout.InfiniteTimeSpan;
            return true;
        }

        internal void Fire(TimeSpan now)
        {
            if (active && now >= dueAt) { active = false; callback(state); }
        }

        public void Dispose() => active = false;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
