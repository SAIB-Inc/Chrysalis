using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Benchmarks;

internal static class Validation
{
    internal static async Task RunAsync()
    {
        Fixtures.Verify();
        await HarnessChecks.RunAsync().ConfigureAwait(false);
        FieldReadBench reader = new();
        reader.Setup();
        long checksum = reader.ReadEveryField();
        foreach (byte[] bytes in Fixtures.Load("block-*.cbor"))
        {
            BlockWithEra original = CborSerializer.Deserialize<BlockWithEra>(bytes);
            BlockWithEra fresh = ModelGraph.Fresh(original);
            if (ModelGraph.Read(original) != ModelGraph.Read(fresh))
            {
                throw new InvalidDataException("Fresh block changed its decoded fields.");
            }
            if (!bytes.AsSpan().SequenceEqual(CborSerializer.Serialize(original)))
            {
                throw new InvalidDataException("Raw block round-trip changed bytes.");
            }
        }
        foreach (byte[] bytes in Fixtures.Load("tx-*.cbor"))
        {
            PostMaryTransaction original = CborSerializer.Deserialize<PostMaryTransaction>(bytes);
            PostMaryTransaction fresh = ModelGraph.Fresh(original);
            if (ModelGraph.Read(original) != ModelGraph.Read(fresh))
            {
                throw new InvalidDataException("Fresh transaction changed its decoded fields.");
            }
            if (!bytes.AsSpan().SequenceEqual(CborSerializer.Serialize(original)))
            {
                throw new InvalidDataException("Raw transaction round-trip changed bytes.");
            }
        }
        _ = Directory.CreateDirectory(Program.ResultsRoot);
        File.WriteAllText(Path.Combine(Program.ResultsRoot, "field-coverage.json"), JsonSerializer.Serialize(ModelGraph.CoveredProperties,
            Program.IndentedJson));
        Console.WriteLine($"Validated all 30 blocks and 164 transactions, {ModelGraph.CoveredProperties.Length} concrete schema properties; checksum {checksum}.");
    }
}

internal static class WirePlaceholder
{
    internal static void Run(string path)
    {
        List<object> rows = [];
        foreach (string file in Directory.GetFiles(Path.Combine(Fixtures.Root, "wire"), "*.cbor").Order(StringComparer.Ordinal))
        {
            string? failure = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (Path.GetFileName(file).Contains("p6", StringComparison.Ordinal))
                {
                    _ = CborSerializer.Deserialize<RejectTx>(bytes).RejectReason.Value.Length;
                }
                else { _ = CborSerializer.Deserialize<Result>(bytes).QueryResult.Raw.Length; }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
            }
            rows.Add(new
            {
                name = Path.GetFileName(file),
                status = "no baseline: path did not work at 7c45d47",
                currentDecodeSucceeded = failure is null,
                failure,
                ns = (double?)null,
                bytes = (double?)null
            });
        }
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rows, Program.IndentedJson));
    }
}
