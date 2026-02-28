using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;

BenchmarkRunner.Run<BlockDeserializationBenchmarks>();

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
                return candidate;
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

    [Benchmark] public BlockWithEra Byron1() => CborSerializer.Deserialize<BlockWithEra>(_byron1);
    [Benchmark] public BlockWithEra Byron7() => CborSerializer.Deserialize<BlockWithEra>(_byron7);
    [Benchmark] public BlockWithEra Genesis() => CborSerializer.Deserialize<BlockWithEra>(_genesis);
    [Benchmark] public BlockWithEra Shelley1() => CborSerializer.Deserialize<BlockWithEra>(_shelley1);
    [Benchmark] public BlockWithEra Allegra1() => CborSerializer.Deserialize<BlockWithEra>(_allegra1);
    [Benchmark] public BlockWithEra Mary1() => CborSerializer.Deserialize<BlockWithEra>(_mary1);
    [Benchmark] public BlockWithEra Alonzo1() => CborSerializer.Deserialize<BlockWithEra>(_alonzo1);
    [Benchmark] public BlockWithEra Alonzo14() => CborSerializer.Deserialize<BlockWithEra>(_alonzo14);
    [Benchmark] public BlockWithEra Babbage1() => CborSerializer.Deserialize<BlockWithEra>(_babbage1);
    [Benchmark] public BlockWithEra Babbage9() => CborSerializer.Deserialize<BlockWithEra>(_babbage9);
    [Benchmark] public BlockWithEra Conway1() => CborSerializer.Deserialize<BlockWithEra>(_conway1);
}
