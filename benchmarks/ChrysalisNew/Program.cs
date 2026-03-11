using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;

BenchmarkRunner.Run(typeof(Program).Assembly, DefaultConfig.Instance);

/// <summary>Block deserialization benchmarks.</summary>
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

    [Benchmark, BenchmarkCategory("Byron 19KB")]
    public BlockWithEra Byron7() => CborSerializer.Deserialize<BlockWithEra>(_byron7);

    [Benchmark, BenchmarkCategory("Alonzo 140KB")]
    public BlockWithEra Alonzo14() => CborSerializer.Deserialize<BlockWithEra>(_alonzo14);

    [Benchmark, BenchmarkCategory("Babbage 160KB")]
    public BlockWithEra Babbage9() => CborSerializer.Deserialize<BlockWithEra>(_babbage9);

    [Benchmark, BenchmarkCategory("Conway 3KB")]
    public BlockWithEra Conway1() => CborSerializer.Deserialize<BlockWithEra>(_conway1);
}

/// <summary>Field access: deserialize + access slot number.</summary>
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

    [Benchmark, BenchmarkCategory("Slot Babbage 160KB")]
    public ulong Slot_Babbage9()
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(_babbage9);
        return BlockExtensions.Slot(block.Block);
    }

    [Benchmark, BenchmarkCategory("TxInputs Babbage 160KB")]
    public int TxInputs_Babbage9()
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(_babbage9);
        return BlockExtensions.TransactionBodies(block.Block).Sum(tx => tx.Inputs().Count());
    }

    [Benchmark, BenchmarkCategory("Slot Conway 3KB")]
    public ulong Slot_Conway1()
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(_conway1);
        return BlockExtensions.Slot(block.Block);
    }
}

/// <summary>Round-trip: deserialize then serialize back.</summary>
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
    public void Setup() => _babbage9 = LoadBlock("babbage9.block");

    private static byte[] LoadBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine(DataDir, filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Benchmark, BenchmarkCategory("RoundTrip Babbage 160KB")]
    public byte[] RoundTrip_Babbage9()
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(_babbage9);
        return CborSerializer.Serialize(block);
    }
}
