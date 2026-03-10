using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

using V1 = Chrysalis.Codec.Serialization.CborSerializer;
using V1Block = Chrysalis.Codec.Types.Cardano.Core.BlockWithEra;
using V1Extensions = Chrysalis.Codec.Extensions.Cardano.Core.BlockExtensions;

using V2 = Chrysalis.Codec.V2.Serialization.CborSerializer;
using V2Block = Chrysalis.Codec.V2.Types.Cardano.Core.BlockWithEra;
using V2Extensions = Chrysalis.Codec.V2.Extensions.Cardano.Core.BlockExtensions;

using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.V2.Extensions.Cardano.Core.Transaction;

BenchmarkRunner.Run(typeof(Program).Assembly, DefaultConfig.Instance);

/// <summary>Block deserialization: V1 (eager records) vs V2 (lazy structs).</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DeserializationBenchmarks
{
    private static string FindDataDir([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? dir = new FileInfo(sourceFile).Directory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find benchmarks/data directory");
    }

    private static readonly string DataDir = FindDataDir();

    private byte[] _byron7 = null!;
    private byte[] _alonzo14 = null!;
    private byte[] _babbage9 = null!;
    private byte[] _conway1 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _byron7 = LoadBlock("byron7.block");
        _alonzo14 = LoadBlock("alonzo14.block");
        _babbage9 = LoadBlock("babbage9.block");
        _conway1 = LoadBlock("conway1.block");
    }

    private static byte[] LoadBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine(DataDir, filename)).Trim();
        return Convert.FromHexString(hex);
    }

    // --- Byron (19KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Byron 19KB")]
    public V1Block V1_Byron7() => V1.Deserialize<V1Block>(_byron7);

    [Benchmark(Description = "V2"), BenchmarkCategory("Byron 19KB")]
    public V2Block V2_Byron7() => V2.Deserialize<V2Block>(_byron7);

    // --- Alonzo (140KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Alonzo 140KB")]
    public V1Block V1_Alonzo14() => V1.Deserialize<V1Block>(_alonzo14);

    [Benchmark(Description = "V2"), BenchmarkCategory("Alonzo 140KB")]
    public V2Block V2_Alonzo14() => V2.Deserialize<V2Block>(_alonzo14);

    // --- Babbage (160KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Babbage 160KB")]
    public V1Block V1_Babbage9() => V1.Deserialize<V1Block>(_babbage9);

    [Benchmark(Description = "V2"), BenchmarkCategory("Babbage 160KB")]
    public V2Block V2_Babbage9() => V2.Deserialize<V2Block>(_babbage9);

    // --- Conway (3KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Conway 3KB")]
    public V1Block V1_Conway1() => V1.Deserialize<V1Block>(_conway1);

    [Benchmark(Description = "V2"), BenchmarkCategory("Conway 3KB")]
    public V2Block V2_Conway1() => V2.Deserialize<V2Block>(_conway1);
}

/// <summary>
/// Field access: deserialize + access slot number.
/// V1 pays full cost upfront; V2 only scans to the slot field.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FieldAccessBenchmarks
{
    private static string FindDataDir([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? dir = new FileInfo(sourceFile).Directory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find benchmarks/data directory");
    }

    private static readonly string DataDir = FindDataDir();

    private byte[] _babbage9 = null!;
    private byte[] _conway1 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _babbage9 = LoadBlock("babbage9.block");
        _conway1 = LoadBlock("conway1.block");
    }

    private static byte[] LoadBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine(DataDir, filename)).Trim();
        return Convert.FromHexString(hex);
    }

    // --- Read slot from Babbage block (160KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Slot Babbage 160KB")]
    public ulong V1_Slot_Babbage9()
    {
        V1Block block = V1.Deserialize<V1Block>(_babbage9);
        return V1Extensions.Slot(block.Block);
    }

    [Benchmark(Description = "V2"), BenchmarkCategory("Slot Babbage 160KB")]
    public ulong V2_Slot_Babbage9()
    {
        V2Block block = V2.Deserialize<V2Block>(_babbage9);
        return V2Extensions.Slot(block.Block);
    }

    // --- Count tx inputs from Babbage block (160KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("TxInputs Babbage 160KB")]
    public int V1_TxInputs_Babbage9()
    {
        V1Block block = V1.Deserialize<V1Block>(_babbage9);
        return V1Extensions.TransactionBodies(block.Block).Sum(tx => tx.Inputs().Count());
    }

    [Benchmark(Description = "V2"), BenchmarkCategory("TxInputs Babbage 160KB")]
    public int V2_TxInputs_Babbage9()
    {
        V2Block block = V2.Deserialize<V2Block>(_babbage9);
        return V2Extensions.TransactionBodies(block.Block).Sum(tx => tx.Inputs().Count());
    }

    // --- Read slot from Conway block (3KB) ---
    [Benchmark(Description = "V1"), BenchmarkCategory("Slot Conway 3KB")]
    public ulong V1_Slot_Conway1()
    {
        V1Block block = V1.Deserialize<V1Block>(_conway1);
        return V1Extensions.Slot(block.Block);
    }

    [Benchmark(Description = "V2"), BenchmarkCategory("Slot Conway 3KB")]
    public ulong V2_Slot_Conway1()
    {
        V2Block block = V2.Deserialize<V2Block>(_conway1);
        return V2Extensions.Slot(block.Block);
    }
}

/// <summary>Round-trip: deserialize then serialize back, verify identical bytes.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RoundTripBenchmarks
{
    private static string FindDataDir([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? dir = new FileInfo(sourceFile).Directory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find benchmarks/data directory");
    }

    private static readonly string DataDir = FindDataDir();

    private byte[] _babbage9 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _babbage9 = LoadBlock("babbage9.block");
    }

    private static byte[] LoadBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine(DataDir, filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Benchmark(Description = "V1"), BenchmarkCategory("RoundTrip Babbage 160KB")]
    public byte[] V1_RoundTrip_Babbage9()
    {
        V1Block block = V1.Deserialize<V1Block>(_babbage9);
        return V1.Serialize(block);
    }

    [Benchmark(Description = "V2"), BenchmarkCategory("RoundTrip Babbage 160KB")]
    public byte[] V2_RoundTrip_Babbage9()
    {
        V2Block block = V2.Deserialize<V2Block>(_babbage9);
        return V2.Serialize(block);
    }
}
