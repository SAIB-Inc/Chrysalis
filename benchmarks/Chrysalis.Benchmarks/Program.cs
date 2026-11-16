using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;

namespace Chrysalis.Benchmarks;

internal static class Program
{
    internal static JsonSerializerOptions IndentedJson { get; } = new() { WriteIndented = true };

    private static readonly string[] RecordedSettings = ["DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_GCHeapHardLimit", "DOTNET_GCServer", "DOTNET_gcServer", "DOTNET_ROLL_FORWARD", "DOTNET_JitObjectStackAllocation", "DOTNET_JitObjectStackAllocationRefClass", "DOTNET_JitDisasm", "CHRYSALIS_DIRECT_WARMUP_MS"];

    private static readonly Type[] BenchmarkTypes =
    [
        typeof(CoreBench), typeof(BlockBench), typeof(TxBench), typeof(VmBench),
        typeof(MuxBench), typeof(NetworkHeaderBench), typeof(CorpusVmBench),
        typeof(FieldReadBench), typeof(EncodingBench), typeof(FixtureHashBench), typeof(ScriptBench)
    ];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        Fixtures.Verify();
        string mode = args.Length > 0 ? args[0] : "full";
        string[] remaining = [.. args.Skip(1)];
        switch (mode)
        {
            case "replay":
                await OfflineReplay.RunAsync().ConfigureAwait(false);
                return 0;
            case "self-test":
                HarnessChecks.Run();
                return 0;
            case "environment":
                Console.WriteLine(JsonSerializer.Serialize(EnvironmentSnapshot()));
                return 0;
            case "direct":
                DirectMeasurements.Run(remaining.FirstOrDefault() ?? "*", Path.Combine(ResultsRoot, "direct.json"));
                return 0;
            case "validate":
                Validation.Run();
                return 0;
            case "wire":
                WirePlaceholder.Run(Path.Combine(ResultsRoot, "wire.json"));
                return 0;
            case "capture-range":
                await WholePath.CaptureAsync(remaining).ConfigureAwait(false);
                return 0;
            case "network":
                await WholePath.RunAsync(remaining).ConfigureAwait(false);
                return 0;
            case "smoke":
            case "full":
                _ = Directory.CreateDirectory(ResultsRoot);
                ManualConfig config = DefaultConfig.Instance.WithOption(ConfigOptions.DisableParallelBuild, true).AddExporter(JsonExporter.Full)
                    .WithArtifactsPath(Path.Combine(ResultsRoot, "bdn"));
                _ = config.AddJob(mode == "smoke"
                    ? Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance)
                        .WithIterationTime(TimeInterval.FromMilliseconds(100)).WithId("Smoke-not-a-gate")
                    : FrameworkToolchain.DefaultJob());
                BenchmarkDotNet.Reports.Summary[] summaries = [.. BenchmarkSwitcher.FromTypes(BenchmarkTypes).Run(remaining, config)];
                return summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors
                    || summary.Reports.Any(report => !report.Success)) ? 1 : 0;
            default:
                throw new ArgumentException($"Unknown benchmark mode: {mode}", nameof(args));
        }
    }

    internal static object EnvironmentSnapshot() => new
    {
        runtime = Environment.Version.ToString(),
        targetFramework = typeof(Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
        framework = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        logicalCpus = Environment.ProcessorCount,
        benchmarkDotNet = typeof(BenchmarkAttribute).Assembly.GetName().Version?.ToString(),
        gc = new
        {
            server = GCSettings.IsServerGC,
            latency = GCSettings.LatencyMode.ToString(),
            concurrent = AppContext.GetData("System.GC.Concurrent"),
            config = GC.GetConfigurationVariables()
        },
        settings = RecordedSettings
                        .ToDictionary(name => name, Environment.GetEnvironmentVariable)
    };

    internal static string ResultsRoot => Environment.GetEnvironmentVariable("CHRYSALIS_BENCHMARK_RESULTS")
        ?? Path.Combine(Fixtures.Root, "..", "out", "results");
}

internal static class ResultSink<T>
{
    internal static T Value { get; set; } = default!;
}

internal static class DirectMeasurements
{
    internal static void Run(string filter, string path)
    {
        int warmupMilliseconds = int.Parse(Environment.GetEnvironmentVariable("CHRYSALIS_DIRECT_WARMUP_MS") ?? "1000", CultureInfo.InvariantCulture);
        List<object> results = [];
        foreach (Type type in new[] { typeof(CoreBench), typeof(BlockBench), typeof(TxBench), typeof(VmBench),
            typeof(NetworkHeaderBench), typeof(CorpusVmBench), typeof(FieldReadBench), typeof(EncodingBench), typeof(FixtureHashBench) })
        {
            object instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.FullName);
            _ = type.GetMethod("Setup")!.Invoke(instance, null);
            try
            {
                foreach (MethodInfo method in type.GetMethods().Where(method => method.IsDefined(typeof(BenchmarkAttribute))))
                {
                    string name = type.Name + "." + method.Name;
                    if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(filter, name))
                    {
                        continue;
                    }
                    Action action = (Action)typeof(DirectMeasurements).GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(type, method.ReturnType).Invoke(null, [instance, method])!;
                    for (int warm = 0; warm < 10; warm++) { action(); }
                    Stopwatch warmup = Stopwatch.StartNew();
                    while (warmup.Elapsed.TotalMilliseconds < warmupMilliseconds)
                    {
                        for (int warm = 0; warm < 32; warm++) { action(); }
                    }
                    string temperature = warmupMilliseconds == 0 ? "cold/transitional: audit-compatible warm-up only"
                        : "after explicit warm-up; steady state not certified";
                    int scale = method.GetCustomAttribute<BenchmarkAttribute>()!.OperationsPerInvoke;
                    List<object> samples = [];
                    for (int batch = 0; batch < 5; batch++)
                    {
                        int count = 1;
                        Stopwatch timer = Stopwatch.StartNew();
                        do
                        {
                            for (int i = 0; i < count; i++) { action(); }
                            count *= 2;
                        } while (timer.Elapsed.TotalMilliseconds < 100 && count < 1 << 20);
                        count = Math.Max(1, count / 2);
                        GC.Collect();
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        timer.Restart();
                        for (int i = 0; i < count; i++) { action(); }
                        timer.Stop();
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        samples.Add(new
                        {
                            batch,
                            temperature,
                            operations = count * scale,
                            ns = timer.Elapsed.TotalNanoseconds / count / scale,
                            bytes = bytes / (double)count / scale
                        });
                    }
                    results.Add(new { name, counter = "GC.GetAllocatedBytesForCurrentThread", warmupMilliseconds, steadyStateCertified = false, samples });
                    Console.WriteLine(name + ": " + JsonSerializer.Serialize(samples));
                }
            }
            finally
            {
                _ = (type.GetMethod("Cleanup")?.Invoke(instance, null));
            }
        }
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(results, Program.IndentedJson));
    }

    private static Action Bind<T, TResult>(object instance, MethodInfo method)
    {
        Func<TResult> action = method.CreateDelegate<Func<TResult>>(instance);
        return () => ResultSink<TResult>.Value = action();
    }
}
