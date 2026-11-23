namespace Chrysalis.Network.Tests;

internal sealed class ManualClock : TimeProvider
{
    internal List<ManualTimer> Timers { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(callback, state, dueTime);
        Timers.Add(timer);
        return timer;
    }
}

internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
{
    internal TimeSpan Due { get; } = due;
    internal bool Disposed { get; private set; }
    internal Action? OnDispose { get; set; }
    internal void Fire() => callback(state);
    public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
    public void Dispose()
    {
        Disposed = true;
        OnDispose?.Invoke();
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        OnDispose?.Invoke();
        return ValueTask.CompletedTask;
    }
}
