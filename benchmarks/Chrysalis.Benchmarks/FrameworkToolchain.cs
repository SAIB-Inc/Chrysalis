using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;

namespace Chrysalis.Benchmarks;

internal static class FrameworkToolchain
{
    // BDN 0.15.8 is the latest stable version, but its SDK validator switches on
    // a runtime enum ending at .NET 10. Use its unchanged standard generator,
    // builder and executor with explicit TFM/runtime instead, on BOTH sides.
    // The runner verifies the staged global.json's actual SDK before building.
    // This changes no warmup, measurement, overhead, GC or diagnoser policy.
    internal static Job DefaultJob()
    {
        string framework = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        NetCoreAppSettings settings = new(framework, Environment.Version.ToString(),
            "Explicit-" + framework, customDotNetCliPath: Environment.ProcessPath,
            packagesPath: Environment.GetEnvironmentVariable("NUGET_PACKAGES"));
        IToolchain standard = CsProjCoreToolchain.From(settings);
        Toolchain toolchain = new(standard.Name, standard.Generator, standard.Builder, standard.Executor);
        return Job.Default.WithRuntime(CoreRuntime.CreateForNewVersion(framework, ".NET " + Environment.Version))
            .WithToolchain(toolchain).WithId("Default-" + framework);
    }
}
