using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;

BenchmarkRunner.Run<BlockDeserializationBenchmarks>();

/// <summary>Benchmarks for block deserialization across all Cardano eras.</summary>
[MemoryDiagnoser]
public class BlockDeserializationBenchmarks
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

    private byte[] _byron1 = null!;
    private byte[] _byron7 = null!;
    private byte[] _genesis = null!;
    private byte[] _shelley1 = null!;
    private byte[] _allegra1 = null!;
    private byte[] _mary1 = null!;
    private byte[] _alonzo1 = null!;
    private byte[] _alonzo14 = null!;
    private byte[] _babbage1 = null!;
    private byte[] _babbage9 = null!;
    private byte[] _conway1 = null!;

    /// <summary>Loads all test block data from hex files.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _byron1 = LoadBlock("byron1.block");
        _byron7 = LoadBlock("byron7.block");
        _genesis = LoadBlock("genesis.block");
        _shelley1 = LoadBlock("shelley1.block");
        _allegra1 = LoadBlock("allegra1.block");
        _mary1 = LoadBlock("mary1.block");
        _alonzo1 = LoadBlock("alonzo1.block");
        _alonzo14 = LoadBlock("alonzo14.block");
        _babbage1 = LoadBlock("babbage1.block");
        _babbage9 = LoadBlock("babbage9.block");
        _conway1 = LoadBlock("conway1.block");
    }

    private static byte[] LoadBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine(DataDir, filename)).Trim();
        return Convert.FromHexString(hex);
    }

    /// <summary>Deserialize Byron era block (variant 1).</summary>
    [Benchmark]
    public BlockWithEra Byron1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_byron1);
    }

    /// <summary>Deserialize Byron era block (variant 7).</summary>
    [Benchmark]
    public BlockWithEra Byron7()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_byron7);
    }

    /// <summary>Deserialize Byron genesis/EBB block.</summary>
    [Benchmark]
    public BlockWithEra Genesis()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_genesis);
    }

    /// <summary>Deserialize Shelley era block.</summary>
    [Benchmark]
    public BlockWithEra Shelley1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_shelley1);
    }

    /// <summary>Deserialize Allegra era block.</summary>
    [Benchmark]
    public BlockWithEra Allegra1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_allegra1);
    }

    /// <summary>Deserialize Mary era block.</summary>
    [Benchmark]
    public BlockWithEra Mary1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_mary1);
    }

    /// <summary>Deserialize Alonzo era block (variant 1).</summary>
    [Benchmark]
    public BlockWithEra Alonzo1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_alonzo1);
    }

    /// <summary>Deserialize Alonzo era block (variant 14).</summary>
    [Benchmark]
    public BlockWithEra Alonzo14()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_alonzo14);
    }

    /// <summary>Deserialize Babbage era block (variant 1).</summary>
    [Benchmark]
    public BlockWithEra Babbage1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_babbage1);
    }

    /// <summary>Deserialize Babbage era block (variant 9).</summary>
    [Benchmark]
    public BlockWithEra Babbage9()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_babbage9);
    }

    /// <summary>Deserialize Conway era block.</summary>
    [Benchmark]
    public BlockWithEra Conway1()
    {
        return CborSerializer.Deserialize<BlockWithEra>(_conway1);
    }
}
