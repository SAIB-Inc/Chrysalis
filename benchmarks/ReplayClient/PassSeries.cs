using System.Diagnostics;
using System.Runtime;

namespace ReplayClient;

/// <summary>Outside-window observations for the fixed, prospectively declared run-three protocol.</summary>
internal sealed class PassSeries
{
    private readonly long _started;
    private RuntimeCounters _previous;
    private long _warmupElapsedTicks;

    internal PassSeries()
    {
        // Prime BOTH observation branches and list-growth helpers before timed work.
        // Otherwise the first measured row can compile an observer helper despite a quiet warmup.
        _started = Stopwatch.GetTimestamp();
        _ = RuntimeCounters.Capture();
        long stamp = Stopwatch.GetTimestamp();
        Add(false, 0, 1, 0, stamp, stamp, 0, 0, 0, 0);
        Add(true, 0, 1, 0, stamp, stamp, 0, 0, 0, 0);
        Rows.Clear();
        WarmupRows.Clear();
        GroupBoundaries.Clear();
        _previous = RuntimeCounters.Capture();
        _started = Stopwatch.GetTimestamp();
        Boundary("warmup", 0);
    }

    internal List<object> WarmupRows { get; } = [];
    internal List<object> Rows { get; } = [];
    internal List<object> GroupBoundaries { get; } = [];
    internal double WarmupElapsedNs => _warmupElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);

    internal void Add(bool warmup, int pass, int blocks, long allocated, long start, long end,
        long wireBytes, int gen0Collections, int gen1Collections, int gen2Collections)
    {
        // Caller has stopped BOTH counters before this method creates row objects or reads GC/JIT metadata.
        object row = new
        {
            pass,
            blocks,
            allocated,
            bytesPerBlock = allocated / (double)blocks,
            nsPerBlock = (end - start) * (1_000_000_000.0 / Stopwatch.Frequency) / blocks,
            wireBytes,
            startTimestamp = start,
            endTimestamp = end,
            elapsedSinceSeriesStartNs = (start - _started) * (1_000_000_000.0 / Stopwatch.Frequency),
            gen0Collections,
            gen1Collections,
            gen2Collections
        };
        if (warmup) { WarmupRows.Add(row); }
        else { Rows.Add(row); }
        Boundary(warmup ? "warmup" : "measured", pass);
        if (warmup && pass == Program.WarmupPasses)
        {
            _warmupElapsedTicks = Stopwatch.GetTimestamp() - _started;
            Boundary("measured", 0);
        }
    }

    private void Boundary(string stage, int passIndex)
    {
        RuntimeCounters current = RuntimeCounters.Capture();
        GroupBoundaries.Add(new
        {
            stage,
            passIndex,
            elapsedNs = (Stopwatch.GetTimestamp() - _started) * (1_000_000_000.0 / Stopwatch.Frequency),
            cumulative = current,
            delta = new RuntimeCounters(current.CompiledMethods - _previous.CompiledMethods,
                current.CompiledILBytes - _previous.CompiledILBytes,
                current.CompilationTimeTicks - _previous.CompilationTimeTicks,
                current.Gen0 - _previous.Gen0, current.Gen1 - _previous.Gen1, current.Gen2 - _previous.Gen2)
        });
        _previous = current;
    }

    private readonly record struct RuntimeCounters(long CompiledMethods, long CompiledILBytes,
        long CompilationTimeTicks, int Gen0, int Gen1, int Gen2)
    {
        internal static RuntimeCounters Capture() => new(JitInfo.GetCompiledMethodCount(),
            JitInfo.GetCompiledILBytes(), JitInfo.GetCompilationTime().Ticks,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }
}
