using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;

BenchmarkRunner.Run<PlutusUseCasesBenchmarks>(
    DefaultConfig.Instance
        .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40)),
    args);

/// <summary>
/// Benchmarks for UPLC CEK machine evaluation using real Plutus smart contract scripts.
/// Test data: 78 flat-encoded scripts from plutuz/bench/plutus_use_cases.
/// Each iteration: flat-decode + CEK evaluate.
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(50)]
public class PlutusUseCasesBenchmarks
{
    private static string FindDataDir([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? dir = new FileInfo(sourceFile).Directory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data", "plutus_use_cases");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find benchmarks/data/plutus_use_cases directory");
    }

    private static readonly string DataDir = FindDataDir();

    private static readonly string[] AllFiles =
        Directory.GetFiles(DataDir, "*.flat")
            .Select(Path.GetFileNameWithoutExtension)
            .Order()
            .ToArray()!;

    private Dictionary<string, byte[]> _flatBytes = null!;

    /// <summary>Loads all flat-encoded script data.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _flatBytes = [];
        foreach (string name in AllFiles)
        {
            _flatBytes[name] = File.ReadAllBytes(Path.Combine(DataDir, name + ".flat"));
        }
    }

    /// <summary>The script name (flat file) to benchmark.</summary>
    [ParamsSource(nameof(ScriptNames))]
    public string Script { get; set; } = "";

    /// <summary>Provides all script names as benchmark parameters.</summary>
    public static IEnumerable<string> ScriptNames() => AllFiles;

    /// <summary>Flat-decode + CEK evaluate a single Plutus script.</summary>
    [Benchmark]
    public Term<DeBruijn> DecodeAndEval()
    {
        byte[] bytes = _flatBytes[Script];
        Program<DeBruijn> program = FlatDecoder.DecodeProgram(bytes);
        CekMachine machine = new(ExBudget.Unlimited);
        return machine.Run(program.Term);
    }
}
