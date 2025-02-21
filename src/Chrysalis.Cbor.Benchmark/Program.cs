using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers;
using Perfolizer.Horology;

var config = DefaultConfig.Instance
    .AddExporter(RPlotExporter.Default)
    .AddExporter(PlainExporter.Default)
    .AddColumn(StatisticColumn.Min)
    .AddColumn(StatisticColumn.Max)
    .WithSummaryStyle(SummaryStyle.Default.WithTimeUnit(TimeUnit.Millisecond))
    .WithOptions(ConfigOptions.DisableOptimizationsValidator)
    .AddDiagnoser(MemoryDiagnoser.Default)  // For memory allocation measurements
    .AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig()))
    .AddJob(Job.Default
        .WithWarmupCount(3)
        .WithIterationCount(10)
        .WithMaxIterationCount(20)
        .WithUnrollFactor(1)
    );

BenchmarkRunner.Run<AlonzoBenchmarks>(config);
BenchmarkRunner.Run<AlonzoDuplicateKeyBenchmarks>(config);
BenchmarkRunner.Run<BabbageBenchmarks>(config);
BenchmarkRunner.Run<ConwayBenchmarks>(config);
