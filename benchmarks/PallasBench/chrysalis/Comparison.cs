using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Benchmarks;

internal static class Comparison
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static long sink;

    internal static async Task RunAsync(string[] args)
    {
        string root = args[0], mode = args[1];
        int iterations = int.Parse(args[2], CultureInfo.InvariantCulture);
        byte[][] blocks = [.. Directory.GetFiles(Path.Combine(root, "fixtures"), "block-*.cbor")
            .Order(StringComparer.Ordinal).Select(File.ReadAllBytes)];
        if (blocks.Length != 30 || iterations < 1) { throw new InvalidDataException("Wrong fixed work."); }
        long items = blocks.Sum(block => ReaderWalk.Count(block));
        object[] checks = [.. blocks.Select(Check)];
        if (mode == "check")
        {
            ReaderWalk.Verify();
            JitWarmup.Verify();
            await JitWarmup.VerifyMeasurementLifetimeAsync().ConfigureAwait(false);
            _ = ReadFields(blocks);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                blocks = blocks.Length,
                items,
                checks,
                properties = ModelGraph.CoveredProperties.Length,
                timeouts = new
                {
                    warmupCapSeconds = JitWarmup.CapSeconds,
                    measuredWindowSeconds = JitWarmup.MeasuredWindowSeconds,
                    marginSeconds = JitWarmup.ProcessMarginSeconds
                }
            }));
            return;
        }
        TimedWindow sample;
        int warmupIterations;
        if (mode == "replay")
        {
            (sample, checks, warmupIterations) = await ReplayAsync(args[3], blocks, iterations).ConfigureAwait(false);
        }
        else
        {
            Func<long> operation = mode switch
            {
                "typed" => () => ReadFields(blocks),
                "walk" => () => blocks.Sum(block => ReaderWalk.Count(block)),
                _ => throw new ArgumentException("Unknown comparison mode.")
            };
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            warmupIterations = await JitWarmup.WaitAsync(() =>
            {
                _ = MicroWindow(operation, 1);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            sample = MicroWindow(operation, iterations);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            serverGC = GCSettings.IsServerGC,
            mode,
            blocks = checks.Length,
            iterations,
            items,
            checks,
            warmupIterations,
            jit = new
            {
                start = sample.Start,
                end = sample.End,
                minimumSeconds = JitWarmup.MinimumSeconds,
                quietSeconds = JitWarmup.QuietSeconds,
                capSeconds = JitWarmup.CapSeconds
            },
            elapsed_ns = sample.Elapsed * (1_000_000_000.0 / Stopwatch.Frequency)
        }, JsonOptions));
    }

    private static long ReadFields(byte[][] blocks)
    {
        long checksum = 0;
        foreach (byte[] bytes in blocks)
        {
            checksum = unchecked(checksum + ModelGraph.Read(CborSerializer.Deserialize<BlockWithEra>(bytes)));
        }
        return checksum;
    }

    private static object Check(byte[] bytes)
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(bytes);
        return new
        {
            slot = block.Block.Slot(),
            hash = block.Block.Hash(),
            transactions = block.Block.TransactionBodies().Count(),
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
        };
    }

    private static TimedWindow MicroWindow(Func<long> operation, int iterations)
    {
        JitSnapshot before = JitSnapshot.Capture();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) { Volatile.Write(ref sink, operation()); }
        long elapsed = Stopwatch.GetTimestamp() - start;
        return new(elapsed, before, JitSnapshot.Capture());
    }

    // Replay peer, not live-node throughput. Connect/intersect/validation are untimed.
    private static async Task<(TimedWindow Sample, object[] Checks, int WarmupIterations)> ReplayAsync(string socket, byte[][] fixtures, int iterations)
    {
        using NodeClient client = await NodeClient.ConnectAsync(socket).ConfigureAwait(false);
        await client.StartAsync(1).ConfigureAwait(false);
        BlockWithEra anchorBlock = CborSerializer.Deserialize<BlockWithEra>(fixtures[0]);
        SpecificPoint anchor = new(anchorBlock.Block.Slot(), Convert.FromHexString(anchorBlock.Block.Hash()));
        byte[][] received = new byte[fixtures.Length - 1][];
        object[] checks = new object[received.Length];
        (TimedWindow sample, int warmup) = await JitWarmup.MeasureAfterAsync(JitWarmup.WaitAsync(async () =>
        {
            _ = await ReplayWindowAsync(client, anchor, fixtures, received, checks, 1, CancellationToken.None).ConfigureAwait(false);
        }), token => ReplayWindowAsync(client, anchor, fixtures, received, checks, iterations, token)).ConfigureAwait(false);
        await client.ChainSync.DoneAsync(CancellationToken.None).ConfigureAwait(false);
        return (sample, checks, warmup);
    }

    private static async Task<TimedWindow> ReplayWindowAsync(NodeClient client, SpecificPoint anchor, byte[][] fixtures,
        byte[][] received, object[] checks, int iterations, CancellationToken token)
    {
        // Counters conservatively bracket the whole sequence, including untimed checks.
        JitSnapshot before = JitSnapshot.Capture();
        long elapsed = 0;
        for (int pass = 0; pass < iterations; pass++)
        {
            ChainSyncMessage intersection = await client.ChainSync.FindIntersectionAsync([anchor], token).ConfigureAwait(false);
            if (intersection is not MessageIntersectFound found || found.Point is not SpecificPoint point
                || point.Slot != anchor.Slot || !point.Hash.Span.SequenceEqual(anchor.Hash.Span))
            {
                throw new InvalidDataException("Replay intersection differs from the fixed anchor.");
            }
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < received.Length; i++)
            {
                MessageNextResponse? message = await client.ChainSync.NextRequestAsync(token).ConfigureAwait(false);
                if (message is not N2CMessageRollForward block) { throw new InvalidDataException("Unexpected replay response."); }
                received[i] = block.Payload.Value.ToArray();
            }
            elapsed += Stopwatch.GetTimestamp() - start;
            for (int i = 0; i < received.Length; i++)
            {
                if (!received[i].AsSpan().SequenceEqual(fixtures[i + 1])) { throw new InvalidDataException("Replay bytes changed."); }
                checks[i] = Check(received[i]);
            }
        }
        return new(elapsed, before, JitSnapshot.Capture());
    }
}
