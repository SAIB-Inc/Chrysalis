using System.Diagnostics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;

string dataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "plutus_use_cases");
if (!Directory.Exists(dataDir))
{
    // Try relative to script location
    dataDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "..", "..", "..", "..", "data", "plutus_use_cases"));
}
if (!Directory.Exists(dataDir))
{
    dataDir = "/home/rawriclark/Projects/Chrysalis/benchmarks/data/plutus_use_cases";
}

string[] files = Directory.GetFiles(dataDir, "*.flat");
Array.Sort(files);
Console.WriteLine($"Found {files.Length} scripts in {dataDir}");

// Pre-load all files
Dictionary<string, byte[]> data = [];
foreach (string file in files)
{
    data[file] = File.ReadAllBytes(file);
}

// Warmup
foreach (string file in files)
{
    Program<DeBruijn> program = FlatDecoder.DecodeProgram(data[file]);
    CekMachine machine = new(ExBudget.Unlimited);
    _ = machine.Run(program.Term);
}

// Benchmark: 10 iterations of all 78 scripts
const int iterations = 10;
Stopwatch sw = Stopwatch.StartNew();
for (int iter = 0; iter < iterations; iter++)
{
    foreach (string file in files)
    {
        Program<DeBruijn> program = FlatDecoder.DecodeProgram(data[file]);
        CekMachine machine = new(ExBudget.Unlimited);
        _ = machine.Run(program.Term);
    }
}
sw.Stop();

double totalMs = sw.Elapsed.TotalMilliseconds;
double perScript = totalMs / (iterations * files.Length);
Console.WriteLine($"Total: {totalMs:F1}ms for {iterations}x{files.Length} = {iterations * files.Length} evaluations");
Console.WriteLine($"Per script (geomean proxy): {perScript * 1000:F1} us");
Console.WriteLine($"GC gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");
