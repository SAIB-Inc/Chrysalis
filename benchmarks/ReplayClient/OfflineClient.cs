using System.Buffers;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Benchmarks;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace ReplayClient;

internal static class OfflineClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private delegate Task<long> ReadPass(bool n2n, IBearer bearer, ChannelBuffer chain, ChannelBuffer fetch,
        byte[][] n2c, byte[][] headers, byte[][] bodies, byte[][] digests, CancellationToken token);

    internal static async Task RunAsync(string output, bool nodeToNode)
    {
        Assembly harness = typeof(CoreBench).Assembly;
        Type fixtures = harness.GetType("Chrysalis.Benchmarks.Fixtures", true)!;
        _ = fixtures.GetMethod("Verify", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        byte[][] blocks = (byte[][])fixtures.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["block-*.cbor"])!;
        byte[][] digests = [.. blocks.Select(SHA256.HashData)];
        byte[][] n2c = new byte[blocks.Length][];
        byte[][] headers = new byte[blocks.Length][];
        byte[][] bodies = new byte[blocks.Length][];
        // Setup is byte-for-byte the original OfflineReplay setup, never part of the counter.
        for (int i = 0; i < blocks.Length; i++)
        {
            BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(blocks[i]);
            Tip tip = new(Point.Specific(block.Block.Slot(), Convert.FromHexString(block.Block.Header().Hash())), 1);
            n2c[i] = Segment(ProtocolType.ClientChainSync, CborSerializer.Serialize(new N2CMessageRollForward(2, new CborEncodedValue(blocks[i]), tip)));
            N2NBlockHeader header = N2NBlockHeader.Create(6, new ShelleyN2NHeader(new CborEncodedValue(block.Block.Header().Raw)));
            headers[i] = Segment(ProtocolType.NodeChainSync, CborSerializer.Serialize(new N2NMessageRollForward(2, header, tip)));
            bodies[i] = Segment(ProtocolType.BlockFetch, CborSerializer.Serialize(new BlockBody(4, new CborEncodedValue(blocks[i]))));
        }
        // 34 corpus cycles = 1020 blocks, the same work as an original offline sample. Only the
        // wrapper's pass boundaries change, allowing the prospective cold/plateau counter protocol.
        n2c = Repeat(n2c); headers = Repeat(headers); bodies = Repeat(bodies); digests = Repeat(digests);
        Type offline = harness.GetType("Chrysalis.Benchmarks.OfflineReplay", true)!;
        Type bearerType = offline.GetNestedType("ReplayBearer", BindingFlags.NonPublic)!;
        MethodInfo method = offline.GetMethod("ReadPassAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type[] types = [typeof(bool), typeof(IBearer), typeof(ChannelBuffer), typeof(ChannelBuffer),
            typeof(byte[][]), typeof(byte[][]), typeof(byte[][]), typeof(byte[][]), typeof(CancellationToken)];
        ParameterExpression[] parameters = [.. types.Select((type, index) => Expression.Parameter(type, "p" + index))];
        Expression[] call = [.. parameters.Cast<Expression>()];
        call[1] = Expression.Convert(parameters[1], bearerType);
        ReadPass read = Expression.Lambda<ReadPass>(Expression.Call(method, call), parameters).Compile();
        using IBearer bearer = (IBearer)Activator.CreateInstance(bearerType, true)!;
        using Plexer plexer = new(bearer);
        ChannelBuffer chain = new(plexer.SubscribeClient(nodeToNode ? ProtocolType.NodeChainSync : ProtocolType.ClientChainSync));
        ChannelBuffer fetch = new(plexer.SubscribeClient(ProtocolType.BlockFetch));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(8));
        Task worker = plexer.RunAsync(cancellation.Token);
        PassSeries series = new();
        try
        {
            long expected = -1;
            for (int index = 1; index <= Program.WarmupPasses + Program.Passes; index++)
            {
                bool warmup = index <= Program.WarmupPasses;
                int pass = warmup ? index : index - Program.WarmupPasses;
                if (!warmup && pass == 1)
                {
                    await Program.MarkAsync("MEASURED_START").ConfigureAwait(false);
                    series.BeginMeasured();
                }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                long before = GC.GetTotalAllocatedBytes(true);
                long start = Stopwatch.GetTimestamp();
                long wireBytes = await read(nodeToNode, bearer, chain, fetch, n2c, headers, bodies, digests, cancellation.Token).ConfigureAwait(false);
                long end = Stopwatch.GetTimestamp();
                long allocated = GC.GetTotalAllocatedBytes(true) - before;
                int delta0 = GC.CollectionCount(0) - gen0, delta1 = GC.CollectionCount(1) - gen1, delta2 = GC.CollectionCount(2) - gen2;
                if (expected < 0) { expected = wireBytes; }
                if (wireBytes != expected) { throw new InvalidDataException("Offline fixed work changed."); }
                series.Add(warmup, pass, n2c.Length, allocated, start, end, wireBytes, delta0, delta1, delta2);
            }
            await Program.MarkAsync("MEASURED_END").ConfigureAwait(false);
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(),
            replay = nodeToNode ? "offline" : "offline-n2c",
            operation = "Unmodified harness OfflineReplay.ReadPassAsync / ReplayBearer, original 30-block corpus repeated34 times/pass",
            limitation = "No fragmented messages: complete single-mux body allocation control, not the captured1000-block fragmentation workload",
            warmupPasses = Program.WarmupPasses,
            passes = Program.Passes,
            warmupRows = series.WarmupRows,
            rows = series.Rows,
            warmupElapsedNs = series.WarmupElapsedNs,
            groupBoundaries = series.GroupBoundaries,
            stopwatchFrequency = Stopwatch.Frequency,
            loadedAssemblies = Program.LoadedAssemblies(),
            processSettings = Program.ProcessSettings
        }, JsonOptions)).ConfigureAwait(false);
    }

    private static byte[][] Repeat(byte[][] data) => [.. Enumerable.Range(0, 34).SelectMany(_ => data)];

    private static byte[] Segment(ProtocolType protocol, byte[] payload) => MuxSegmentCodec.Encode(new MuxSegment(
        new MuxSegmentHeader(0, protocol, checked((ushort)payload.Length), false), new ReadOnlySequence<byte>(payload))).ToArray();
}
