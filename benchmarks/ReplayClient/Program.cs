using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;

namespace ReplayClient;

internal static class Program
{
    internal static int WarmupPasses { get; private set; }
    internal static int Passes { get; private set; }

    internal static object ProcessSettings { get; } = CaptureProcessSettings();

    private static object CaptureProcessSettings()
    {
        if (Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(key =>
            key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) || key.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_KEY", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Credential-bearing process environment is forbidden.");
        }
        string[] keys = ["System.Runtime.TieredPGO", "System.Runtime.TieredCompilation",
            "System.Runtime.TieredCompilation.QuickJit", "System.Runtime.TieredCompilation.QuickJitForLoops",
            "System.GC.Server", "System.GC.Concurrent", "RUNTIME_IDENTIFIER", "APP_CONTEXT_BASE_DIRECTORY",
            "TRUSTED_PLATFORM_ASSEMBLIES", "NATIVE_DLL_SEARCH_DIRECTORIES"];
        return new
        {
            frameworkDescription = RuntimeInformation.FrameworkDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            appContextData = keys.ToDictionary(key => key, AppContext.GetData, StringComparer.Ordinal),
            appContextSwitches = keys.Select(key =>
            {
                bool present = AppContext.TryGetSwitch(key, out bool enabled);
                return new
                {
                    key,
                    present,
                    enabled
                };
            }).ToArray(),
            effectivePgoInterpretation = "Explicit AppContext values recorded; absent defaults are not directly exposed by a public runtime query and are not silently invented.",
            gcIsServer = GCSettings.IsServerGC,
            gcLatencyMode = GCSettings.LatencyMode.ToString(),
            gcLohCompactionMode = GCSettings.LargeObjectHeapCompactionMode.ToString(),
            gcConfiguration = GC.GetConfigurationVariables()
        };
    }

    internal static object[] LoadedAssemblies() => [.. AppDomain.CurrentDomain.GetAssemblies()
        .OrderBy(assembly => assembly.FullName, StringComparer.Ordinal)
        .Select(assembly => (object)new
        {
            identity = assembly.FullName,
            path = assembly.Location
        })];

    private static bool Observed { get; set; }

    internal static async Task MarkAsync(string mark)
    {
        if (!Observed) { return; }
        Console.WriteLine(mark);
        _ = await Task.Run(Console.ReadLine).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false) ?? throw new EndOfStreamException();
    }

    private static async Task<int> Main(string[] args)
    {
        _ = ProcessSettings;
        Observed = args.Length == 5 && args[4] == "--observed";
        if ((args.Length != 4 && !Observed) || args[0] is not ("tcp" or "offline" or "offline-n2c") || string.IsNullOrWhiteSpace(args[1]) ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int warmup) || warmup <= 0 ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int measured) || measured <= 0 ||
            warmup >= int.MaxValue - measured)
        {
            throw new ArgumentException("Expected tcp|offline|offline-n2c OUTPUT_JSON WARMUP MEASURED [--observed], with positive integer counts and total below Int32.MaxValue.", nameof(args));
        }
        WarmupPasses = warmup;
        Passes = measured;
        try
        {
            if (args[0] == "tcp") { await TcpClient.RunAsync(args[1]).ConfigureAwait(false); }
            else { await OfflineClient.RunAsync(args[1], args[0] != "offline-n2c").ConfigureAwait(false); }
            return 0;
        }
        catch (Exception exception) when (Observed && exception is TimeoutException or EndOfStreamException)
        {
            await Console.Error.WriteLineAsync("Measured-window acknowledgement missing.").ConfigureAwait(false);
            return 3;
        }
    }
}
