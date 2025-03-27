extern alias Old;
extern alias New;

using BenchmarkDotNet.Attributes;
using NewSerializer = New::Chrysalis.Cbor.Serialization.CborSerializer;
using OldSerializer = Old::Chrysalis.Cbor.Converters.CborSerializer;
using NewBlock = New::Chrysalis.Cbor.Types.Cardano.Core.Block;
using OldBlock = Old::Chrysalis.Cardano.Core.Types.Block.BabbageBlock;

namespace Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers;

public class BabbageBenchmarks
{
    private const int CONCURRENCY_LEVEL = 2;
    private const int ITERATIONS_PER_TASK = 2;

    private readonly string _block = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "data", "babbage_block.cbor"));

    [Benchmark]
    public async Task New()
    {
        byte[] cborBytes = Convert.FromHexString(_block);


        IEnumerable<Task> tasks = [.. Enumerable.Range(0, CONCURRENCY_LEVEL).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < ITERATIONS_PER_TASK; i++)
                NewSerializer.Deserialize<NewBlock>(cborBytes);
        }))];

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task Old()
    {
        byte[] cborBytes = Convert.FromHexString(_block);

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, CONCURRENCY_LEVEL).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < ITERATIONS_PER_TASK; i++)
                OldSerializer.Deserialize<OldBlock>(cborBytes);
        }))];

        await Task.WhenAll(tasks);
    }
}