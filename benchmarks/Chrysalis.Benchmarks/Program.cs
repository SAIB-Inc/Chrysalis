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

    internal static readonly Type[] DirectTypes =
    [
        typeof(CoreBench), typeof(BlockBench), typeof(TxBench), typeof(VmBench),
        typeof(NetworkHeaderBench), typeof(CorpusVmBench), typeof(FieldReadBench),
        typeof(EncodingBench), typeof(FixtureHashBench)
    ];

    private static readonly Type[] BenchmarkTypes = [.. DirectTypes, typeof(MuxBench), typeof(ScriptBench)];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "full";
        string[] remaining = [.. args.Skip(1)];
        if (mode == "mac-probes")
        {
            RequireNativeMac();
            if (remaining.Length != 0)
            {
                throw new ArgumentException("mac-probes does not accept arguments", nameof(args));
            }
            _ = Directory.CreateDirectory(ResultsRoot);
            File.WriteAllText(Path.Combine(ResultsRoot, "mac-probes.json"), JsonSerializer.Serialize(DirectMeasurements.MacProbeNames()));
            return 0;
        }
        Fixtures.Verify();
        switch (mode)
        {
            case "replay":
                await OfflineReplay.RunAsync().ConfigureAwait(false);
                return 0;
            case "self-test":
                await HarnessChecks.RunAsync().ConfigureAwait(false);
                return 0;
            case "environment":
                Console.WriteLine(JsonSerializer.Serialize(EnvironmentSnapshot()));
                return 0;
            case "direct":
                DirectMeasurements.Run(remaining.FirstOrDefault() ?? "*", Path.Combine(ResultsRoot, "direct.json"));
                return 0;
            case "mac-bytes":
                RequireNativeMac();
                if (remaining.Length != 3 || !int.TryParse(remaining[2], CultureInfo.InvariantCulture, out int batches) || batches < 1)
                {
                    throw new ArgumentException("mac-bytes requires a filter, fixed invocation counts and a batch count", nameof(args));
                }
                Dictionary<string, int> counts = JsonSerializer.Deserialize<Dictionary<string, int>>(remaining[1])
                    ?? throw new ArgumentException("Missing Mac byte invocation counts", nameof(args));
                if (counts.Count == 0 || counts.Values.Any(count => count is < 1 or > (1 << 20)))
                {
                    throw new ArgumentException("Invalid Mac byte invocation counts", nameof(args));
                }
                DirectMeasurements.Run(remaining[0], Path.Combine(ResultsRoot, "direct.json"), counts, batches);
                return 0;
            case "validate":
                await Validation.RunAsync().ConfigureAwait(false);
                return 0;
            case "wire":
                WirePlaceholder.Run(Path.Combine(ResultsRoot, "wire.json"));
                return 0;
            case "corpus":
                await CorpusCapture.RunAsync(remaining).ConfigureAwait(false);
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

    private static void RequireNativeMac()
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException("Native macOS arm64 is required");
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
    internal static bool Matches(string filter, string name) => filter.Split(',')
        .Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name));

    private static IEnumerable<MethodInfo> ProbeMethods(Type type) => type.GetMethods()
        .Where(method => method.IsDefined(typeof(BenchmarkAttribute)));

    private static string ProbeName(Type type, MethodInfo method) => type.Name + "." + method.Name;

    private static IEnumerable<Type> MacProbeTypes(string filter) => Program.DirectTypes.Union(typeof(Program).Assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && ProbeMethods(type).Any(method => Matches(filter, ProbeName(type, method)))));

    internal static IEnumerable<string> MacProbeNames() => MacProbeTypes("*")
        .SelectMany(type => ProbeMethods(type).Select(method => ProbeName(type, method))).Order(StringComparer.Ordinal);

    internal static void Run(string filter, string path, IReadOnlyDictionary<string, int>? fixedCounts = null, int fixedBatchCount = 0)
    {
        int warmupMilliseconds = int.Parse(Environment.GetEnvironmentVariable("CHRYSALIS_DIRECT_WARMUP_MS") ?? "1000", CultureInfo.InvariantCulture);
        List<object> results = [];
        IEnumerable<Type> types = fixedCounts is null ? Program.DirectTypes : MacProbeTypes(filter);
        int batchCount = fixedCounts is null ? 5 : fixedBatchCount;
        foreach (Type type in types)
        {
            object instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.FullName);
            MethodInfo? setup = fixedCounts is null ? type.GetMethod("Setup") : type.GetMethod("Setup", Type.EmptyTypes);
            if (fixedCounts is not null && setup is null)
            {
                throw new InvalidOperationException($"Mac direct probe class {type.FullName} requires a public parameterless Setup()");
            }
            _ = setup!.Invoke(instance, null);
            try
            {
                foreach (MethodInfo method in ProbeMethods(type))
                {
                    string name = ProbeName(type, method);
                    if (!Matches(filter, name))
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
                    for (int batch = 0; batch < batchCount; batch++)
                    {
                        if (fixedCounts is not null)
                        {
                            samples.Add(MeasureFixed(action, fixedCounts[name], scale, batch, temperature));
                            continue;
                        }
                        int count = 1;
                        Stopwatch timer = Stopwatch.StartNew();
                        do
                        {
                            for (int i = 0; i < count; i++) { action(); }
                            count *= 2;
                        } while (timer.Elapsed.TotalMilliseconds < 100 && count < 1 << 20);
                        count = Math.Max(1, count / 2);
                        GC.Collect();
                        long jitCompiledMethodsStart = JitInfo.GetCompiledMethodCount();
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        timer.Restart();
                        for (int i = 0; i < count; i++) { action(); }
                        timer.Stop();
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        long jitCompiledMethodsEnd = JitInfo.GetCompiledMethodCount();
                        samples.Add(new
                        {
                            batch,
                            jitCompiledMethodsStart,
                            jitCompiledMethodsEnd,
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

    private static object MeasureFixed(Action action, int count, int scale, int batch, string temperature)
    {
        // Prime with the old modal calibration's volume, without using a clock to choose it.
        for (int i = 0; i < ((2 * count) - 1); i++) { action(); }
        GC.Collect();
        long jitCompiledMethodsStart = JitInfo.GetCompiledMethodCount();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) { action(); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        long jitCompiledMethodsEnd = JitInfo.GetCompiledMethodCount();
        return new
        {
            batch,
            jitCompiledMethodsStart,
            jitCompiledMethodsEnd,
            temperature,
            invocations = count,
            operations = count * scale,
            bytes = bytes / (double)count / scale
        };
    }

    private static Action Bind<T, TResult>(object instance, MethodInfo method)
    {
        Func<TResult> action = method.CreateDelegate<Func<TResult>>(instance);
        return () => ResultSink<TResult>.Value = action();
    }
}
