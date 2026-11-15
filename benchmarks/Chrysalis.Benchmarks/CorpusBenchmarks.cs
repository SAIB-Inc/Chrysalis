using BenchmarkDotNet.Attributes;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Benchmarks;

/// <summary>Reads all schema properties, recursively, for every fixed audit block.</summary>
[MemoryDiagnoser]
public class FieldReadBench
{
    private byte[][] blocks = null!;

    /// <summary>Loads the corpus and compiles typed property visitors outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        blocks = Fixtures.Load("block-*.cbor");
        _ = ReadEveryField();
    }

    /// <summary>Includes block decode, every header/body/output/witness/governance/metadata field, and opaque data tokens.</summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public long ReadEveryField()
    {
        long checksum = 0;
        foreach (byte[] bytes in blocks)
        {
            checksum = unchecked(checksum + ModelGraph.Read(CborSerializer.Deserialize<BlockWithEra>(bytes)));
        }
        return checksum;
    }
}

/// <summary>Separates preserved-raw serialization from recursive fresh model reconstruction and encoding.</summary>
[MemoryDiagnoser]
public class EncodingBench
{
    private BlockWithEra[] blocks = null!;
    private PostMaryTransaction[] transactions = null!;

    /// <summary>Pre-decodes fixture roots and compiles reconstruction delegates outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        blocks = [.. Fixtures.Load("block-*.cbor").Select(bytes => CborSerializer.Deserialize<BlockWithEra>(bytes))];
        transactions = [.. Fixtures.Load("tx-*.cbor").Select(bytes => CborSerializer.Deserialize<PostMaryTransaction>(bytes))];
        _ = FreshBlocks();
        _ = FreshTransactions();
    }

    /// <summary>Copies original encodings without walking their fields.</summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public long RawBlocks()
    {
        long count = 0;
        foreach (BlockWithEra block in blocks) { count += CborSerializer.Serialize(block).Length; }
        return count;
    }

    /// <summary>Copies original transaction encodings without walking their fields.</summary>
    [Benchmark(OperationsPerInvoke = 164)]
    public long RawTransactions()
    {
        long count = 0;
        foreach (PostMaryTransaction transaction in transactions) { count += CborSerializer.Serialize(transaction).Length; }
        return count;
    }

    /// <summary>Recursively rebuilds every model through typed Create factories; no original Raw shortcut is used.</summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public long FreshBlocks()
    {
        long count = 0;
        foreach (BlockWithEra block in blocks) { count += CborSerializer.Serialize(ModelGraph.Fresh(block)).Length; }
        return count;
    }

    /// <summary>Recursively rebuilds transaction bodies, witnesses and auxiliary data before encoding.</summary>
    [Benchmark(OperationsPerInvoke = 164)]
    public long FreshTransactions()
    {
        long count = 0;
        foreach (PostMaryTransaction transaction in transactions) { count += CborSerializer.Serialize(ModelGraph.Fresh(transaction)).Length; }
        return count;
    }
}

/// <summary>Hashes the actual headers and bodies of the fixed corpus.</summary>
[MemoryDiagnoser]
public class FixtureHashBench
{
    private ReadOnlyMemory<byte>[] headers = null!;
    private ReadOnlyMemory<byte>[] bodies = null!;

    /// <summary>Extracts preserved header/body slices outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        headers = [.. Fixtures.Load("block-*.cbor").Select(bytes => CborSerializer.Deserialize<BlockWithEra>(bytes).Block.Header().Raw)];
        bodies = [.. Fixtures.Load("txbody-*.cbor").Select(bytes => (ReadOnlyMemory<byte>)bytes)];
    }

    /// <summary>Returns total digest lengths to consume all Blake2b-256 results.</summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public int Headers256() => Hash(headers, 32);

    /// <summary>Returns total digest lengths to consume all Blake2b-224 results.</summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public int Headers224() => Hash(headers, 28);

    /// <summary>Hashes all 164 transaction bodies with Blake2b-256.</summary>
    [Benchmark(OperationsPerInvoke = 164)]
    public int Bodies256() => Hash(bodies, 32);

    /// <summary>Hashes all 164 transaction bodies with Blake2b-224.</summary>
    [Benchmark(OperationsPerInvoke = 164)]
    public int Bodies224() => Hash(bodies, 28);

    private static int Hash(ReadOnlyMemory<byte>[] inputs, int size)
    {
        int sum = 0;
        foreach (ReadOnlyMemory<byte> input in inputs) { sum += Blake2Fast.Blake2b.HashData(size, input.Span).Length; }
        return sum;
    }
}

/// <summary>Separates Flat syntax decoding from CEK evaluation for each of the 78 repository scripts.</summary>
[MemoryDiagnoser]
public class ScriptBench
{
    private byte[] bytes = null!;
    private Program<DeBruijn> program = null!;

    /// <summary>Gets the immutable repository script names.</summary>
    public static IEnumerable<string> ScriptNames => Directory.GetFiles(Path.Combine(Fixtures.Root, "../data/plutus_use_cases"), "*.flat")
        .Order(StringComparer.Ordinal).Select(Path.GetFileNameWithoutExtension)!;

    /// <summary>Gets or sets the script selected by BenchmarkDotNet.</summary>
    [ParamsSource(nameof(ScriptNames))]
    public string Script { get; set; } = "escrow-redeem_1-1";

    /// <summary>Loads bytes and decodes the syntax tree outside evaluation timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        bytes = File.ReadAllBytes(Path.Combine(Fixtures.Root, "../data/plutus_use_cases", Script + ".flat"));
        program = FlatDecoder.DecodeProgram(bytes);
    }

    /// <summary>Only parses Flat; no CEK machine is constructed.</summary>
    [Benchmark]
    public Program<DeBruijn> FlatDecode() => FlatDecoder.DecodeProgram(bytes);

    /// <summary>Creates a fresh machine and evaluates the predecoded syntax tree.</summary>
    [Benchmark]
    public Term<DeBruijn> Evaluate() => new CekMachine(ExBudget.Unlimited).Run(program.Term);
}
