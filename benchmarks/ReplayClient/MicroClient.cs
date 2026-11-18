using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Chrysalis.Benchmarks;

namespace ReplayClient;

/// <summary>Fixed hot-path work, using the direct harness's types, binding and sink.</summary>
internal static class MicroClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static async Task RunAsync(string output, string filter, int invocations)
    {
        Assembly harness = typeof(CoreBench).Assembly;
        Type program = harness.GetType("Chrysalis.Benchmarks.Program", true)!;
        Type direct = harness.GetType("Chrysalis.Benchmarks.DirectMeasurements", true)!;
        Type[] types = (Type[])program.GetField("DirectTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        MethodInfo matches = direct.GetMethod("Matches", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo bind = direct.GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic)!;
        List<(Action Action, int Scale, string Name)> work = [];
        List<object> instances = [];
        try
        {
            foreach (Type type in types)
            {
                MethodInfo[] methods = [.. type.GetMethods().Where(method => method.IsDefined(typeof(BenchmarkAttribute))
                    && (bool)matches.Invoke(null, [filter, type.Name + "." + method.Name])!)];
                if (methods.Length == 0) { continue; }
                object instance = Activator.CreateInstance(type)!;
                instances.Add(instance);
                _ = type.GetMethod("Setup")!.Invoke(instance, null);
                foreach (MethodInfo method in methods)
                {
                    Action action = (Action)bind.MakeGenericMethod(type, method.ReturnType).Invoke(null, [instance, method])!;
                    work.Add((action, method.GetCustomAttribute<BenchmarkAttribute>()!.OperationsPerInvoke, type.Name + "." + method.Name));
                }
            }
            if (work.Count == 0) { throw new ArgumentException("Micro filter matched no benchmark.", nameof(filter)); }
            // Constant work per pass, not a per-side adaptive calibration.
            int operations = checked(work.Sum(item => item.Scale) * invocations);
            PassSeries series = new();
            for (int index = 1; index <= Program.WarmupPasses + Program.Passes; index++)
            {
                bool warmup = index <= Program.WarmupPasses;
                int pass = warmup ? index : index - Program.WarmupPasses;
                if (!warmup && pass == 1)
                {
                    await Program.MarkAsync("MEASURED_START").ConfigureAwait(false);
                    series.BeginMeasured();
                }
                int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                long before = GC.GetTotalAllocatedBytes(true);
                long start = Stopwatch.GetTimestamp();
                foreach ((Action action, _, _) in work)
                {
                    for (int invocation = 0; invocation < invocations; invocation++) { action(); }
                }
                long end = Stopwatch.GetTimestamp();
                long allocated = GC.GetTotalAllocatedBytes(true) - before;
                series.Add(warmup, pass, operations, allocated, start, end, 0,
                    GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
            }
            await Program.MarkAsync("MEASURED_END").ConfigureAwait(false);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                runtime = Environment.Version.ToString(),
                replay = "micro",
                filter,
                benchmarks = work.Select(item => item.Name).ToArray(),
                invocationsPerPass = invocations,
                warmupPasses = Program.WarmupPasses,
                passes = Program.Passes,
                warmupRows = series.WarmupRows,
                rows = series.Rows,
                warmupElapsedNs = series.WarmupElapsedNs,
                groupBoundaries = series.GroupBoundaries,
                loadedAssemblies = Program.LoadedAssemblies(),
                processSettings = Program.ProcessSettings
            }, JsonOptions)).ConfigureAwait(false);
        }
        finally
        {
            foreach (object instance in instances) { _ = instance.GetType().GetMethod("Cleanup")?.Invoke(instance, null); }
        }
    }
}
