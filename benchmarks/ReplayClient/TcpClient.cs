using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Chrysalis.Benchmarks;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

namespace ReplayClient;

internal static class TcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static async Task RunAsync(string output)
    {
        Assembly harness = typeof(CoreBench).Assembly;
        Type rangeType = harness.GetType("Chrysalis.Benchmarks.FixedRange", true)!;
        string path = Environment.GetEnvironmentVariable("P1_RANGE") ?? throw new InvalidOperationException("P1_RANGE is required.");
        object range = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path).ConfigureAwait(false), rangeType) ?? throw new InvalidDataException("Range missing.");
        object anchor = rangeType.GetProperty("Anchor")!.GetValue(range)!;
        Array blocks = (Array)rangeType.GetProperty("Blocks")!.GetValue(range)!;
        if (blocks.Length != 1000) { throw new InvalidDataException("The declared TCP range has1000 blocks."); }
        Type pointType = anchor.GetType();
        byte[][] digests = [.. blocks.Cast<object>().Select(point => Convert.FromHexString((string)pointType.GetProperty("Sha256")!.GetValue(point)!))];
        Type whole = harness.GetType("Chrysalis.Benchmarks.WholePath", true)!;
        ParameterExpression peerArg = Expression.Parameter(typeof(PeerClient));
        ParameterExpression pointsArg = Expression.Parameter(typeof(ChainPoint[]));
        ParameterExpression tokenArg = Expression.Parameter(typeof(CancellationToken));
        MethodInfo readMethod = whole.GetMethod("ReadHeadersAndFetchAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Func<PeerClient, ChainPoint[], CancellationToken, Task<long>> read = Expression.Lambda<Func<PeerClient, ChainPoint[], CancellationToken, Task<long>>>(
            Expression.Call(readMethod, peerArg, pointsArg, Expression.Constant(range, rangeType), Expression.Constant(digests), tokenArg), peerArg, pointsArg, tokenArg).Compile();
        ParameterExpression chainArg = Expression.Parameter(typeof(ChainSync));
        MethodInfo intersectMethod = whole.GetMethod("IntersectAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Func<ChainSync, CancellationToken, Task> intersect = Expression.Lambda<Func<ChainSync, CancellationToken, Task>>(
            Expression.Call(intersectMethod, chainArg, Expression.Constant(anchor, pointType), tokenArg), chainArg, tokenArg).Compile();
        Action<ChainPoint[]> validate = Expression.Lambda<Action<ChainPoint[]>>(
            Expression.Call(whole.GetMethod("ValidatePoints", BindingFlags.NonPublic | BindingFlags.Static)!, pointsArg, Expression.Constant(range, rangeType)), pointsArg).Compile();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(8));
        using PeerClient client = await PeerClient.ConnectAsync("127.0.0.2", 3001, cancellation.Token).ConfigureAwait(false);
        await client.StartAsync(1, TimeSpan.FromHours(1)).ConfigureAwait(false);
        ChainPoint[] points = new ChainPoint[blocks.Length];
        PassSeries series = new();
        for (int index = 1; index <= Program.WarmupPasses + Program.Passes; index++)
        {
            bool warmup = index <= Program.WarmupPasses;
            int pass = warmup ? index : index - Program.WarmupPasses;
            if (!warmup && pass == 1)
            {
                await Program.MarkAsync("MEASURED_START").ConfigureAwait(false);
                series.BeginMeasured();
            }
            Array.Clear(points);
            await intersect(client.ChainSync, cancellation.Token).ConfigureAwait(false);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long before = GC.GetTotalAllocatedBytes(true);
            long start = Stopwatch.GetTimestamp();
            long bytes = await read(client, points, cancellation.Token).ConfigureAwait(false);
            long end = Stopwatch.GetTimestamp();
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            int delta0 = GC.CollectionCount(0) - gen0, delta1 = GC.CollectionCount(1) - gen1, delta2 = GC.CollectionCount(2) - gen2;
            validate(points);
            if (bytes != 4_040_750) { throw new InvalidDataException("TCP raw block bytes changed."); }
            series.Add(warmup, pass, points.Length, allocated, start, end, bytes, delta0, delta1, delta2);
        }
        await Program.MarkAsync("MEASURED_END").ConfigureAwait(false);
        await client.ChainSync.DoneAsync(cancellation.Token).ConfigureAwait(false);
        await client.BlockFetch.DoneAsync(cancellation.Token).ConfigureAwait(false);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(),
            operation = "Unmodified WholePath.ReadHeadersAndFetchAsync over recorded real TCP loopback",
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
}
