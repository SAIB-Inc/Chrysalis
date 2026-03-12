using System.Diagnostics;
using System.Globalization;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

const string DefaultHost = "127.0.0.1";
const int DefaultPort = 3001;
const ulong DefaultMagic = 2;
const int DefaultBlockCount = 10000;
const int DefaultBatchSize = 100;
const int ReportInterval = 1000;

// Parse args
Dictionary<string, string> args_ = ParseArgs(args);
string host = GetArg(args_, "tcp-host", DefaultHost);
int port = int.Parse(GetArg(args_, "tcp-port", DefaultPort.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
ulong magic = ulong.Parse(GetArg(args_, "magic", DefaultMagic.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
int targetBlocks = int.Parse(GetArg(args_, "blocks", DefaultBlockCount.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
int batchSize = int.Parse(GetArg(args_, "batch", DefaultBatchSize.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
string? socketPath = GetArgOrNull(args_, "socket");
string? fromSlotStr = GetArgOrNull(args_, "slot");
string? fromHash = GetArgOrNull(args_, "hash");
bool noDeser = GetArgOrNull(args_, "no-deser") is not null;
bool useUnixSocket = socketPath is not null;

if (useUnixSocket)
{
    Console.WriteLine($"ChainSync Benchmark (N2C Unix Socket)");
    Console.WriteLine($"  Socket:     {socketPath}");
}
else
{
    Console.WriteLine($"ChainSync + BlockFetch Benchmark (N2N TCP)");
    Console.WriteLine($"  Node:       {host}:{port}");
}
Console.WriteLine($"  Magic:      {magic}");
Console.WriteLine($"  Target:     {targetBlocks} blocks (batch size {batchSize})");
Console.WriteLine();

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Connect
ChainSync chainSync;
BlockFetch? blockFetch = null;
IDisposable client;

if (useUnixSocket)
{
    NodeClient node = await NodeClient.ConnectAsync(socketPath!, cts.Token).ConfigureAwait(false);
    await node.StartAsync(magic).ConfigureAwait(false);
    chainSync = node.ChainSync;
    client = node;
}
else
{
    PeerClient peer = await PeerClient.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
    await peer.StartAsync(magic).ConfigureAwait(false);
    chainSync = peer.ChainSync;
    blockFetch = peer.BlockFetch;
    client = peer;
}

using (client)
{
    Console.WriteLine("Connected. Starting sync from origin...");
    Console.WriteLine();

    Point startPoint = (fromSlotStr is not null && fromHash is not null)
        ? Point.Specific(ulong.Parse(fromSlotStr, CultureInfo.InvariantCulture), Convert.FromHexString(fromHash))
        : Point.Origin;
    if (startPoint != Point.Origin)
    {
        Console.WriteLine($"  Starting from slot {fromSlotStr}");
        Console.WriteLine();
    }
    ChainSyncMessage intersect = await chainSync.FindIntersectionAsync([startPoint], cts.Token).ConfigureAwait(false);
    Console.WriteLine($"Intersection response type: {intersect?.GetType().FullName ?? "null"}");
    if (intersect is not null)
    {
        Console.WriteLine($"Intersection raw hex: {Convert.ToHexString(intersect.Raw.Span[..Math.Min(100, intersect.Raw.Length)])}");
    }
    if (intersect is not MessageIntersectFound)
    {
        Console.Error.WriteLine("Failed to find intersection.");
        return 1;
    }

    Stopwatch totalTimer = Stopwatch.StartNew();
    Stopwatch windowTimer = Stopwatch.StartNew();

    int totalBlocksSynced = 0;
    long totalBytesDownloaded = 0;
    int windowBlocks = 0;
    long windowBytes = 0;
    string lastEra = "?";
    ulong lastSlot = 0;
    ulong lastBlockNumber = 0;
    int deserErrors = 0;
    Dictionary<string, int> errorBuckets = [];

    try
    {
        while (totalBlocksSynced < targetBlocks && !cts.Token.IsCancellationRequested)
        {
            if (useUnixSocket)
            {
                // N2C mode: each RollForward payload IS the full block
                MessageNextResponse? response = await chainSync.NextRequestAsync(cts.Token).ConfigureAwait(false);

                switch (response)
                {
                    case MessageRollForward rollForward:
                        int payloadLen = rollForward.Payload.Value.Length;

                        if (!noDeser)
                        {
                            try
                            {
                                BlockWithEra block = rollForward.Payload.Deserialize<BlockWithEra>();
                                lastEra = block.Block?.GetType().Name ?? "?";
                            }
                            catch (Exception ex)
                            {
                                lastEra = "?";
                                deserErrors++;
                                string key = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                                errorBuckets[key] = errorBuckets.GetValueOrDefault(key) + 1;
                                if (deserErrors <= 3)
                                {
                                    ReadOnlyMemory<byte> raw = rollForward.Payload.Value;
                                    Console.Error.WriteLine($"  DESER ERROR #{deserErrors} at block {totalBlocksSynced}: {ex.Message}");
                                    Exception? cur = ex;
                                    while (cur != null)
                                    {
                                        Console.Error.WriteLine($"    [{cur.GetType().Name}] {cur.Message}");
                                        Console.Error.WriteLine($"      at: {cur.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                                        cur = cur.InnerException;
                                    }
                                    Console.Error.WriteLine($"    Payload len={raw.Length}, first 60 bytes: {Convert.ToHexString(raw.Span[..Math.Min(60, raw.Length)])}");
                                    await File.WriteAllBytesAsync($"/tmp/failing_block_{deserErrors}.cbor", raw.ToArray(), cts.Token).ConfigureAwait(false);
                                    Console.Error.WriteLine($"    Saved to /tmp/failing_block_{deserErrors}.cbor");
                                }
                            }
                        }

                        totalBlocksSynced++;
                        windowBlocks++;
                        totalBytesDownloaded += payloadLen;
                        windowBytes += payloadLen;

                        if (totalBlocksSynced % ReportInterval == 0)
                        {
                            PrintProgress(totalTimer, windowTimer, lastSlot, lastBlockNumber, lastEra,
                                windowBlocks, windowBytes, totalBytesDownloaded);
                            windowTimer.Restart();
                            windowBlocks = 0;
                            windowBytes = 0;
                        }
                        break;

                    case MessageRollBackward:
                        break;

                    case MessageAwaitReply:
                        Console.WriteLine("Reached chain tip.");
                        goto done;

                    default:
                        break;
                }

                continue;
            }

            // N2N mode: ChainSync headers → BlockFetch full blocks
            int remaining = targetBlocks - totalBlocksSynced;
            int thisBatch = Math.Min(batchSize, remaining);

            List<(Point Point, string Era, ulong Slot, ulong BlockNum)> headerBatch = new(thisBatch);

            while (headerBatch.Count < thisBatch && !cts.Token.IsCancellationRequested)
            {
                MessageNextResponse? response = await chainSync.NextRequestAsync(cts.Token).ConfigureAwait(false);

                switch (response)
                {
                    case MessageRollForward rollForward:
                        (Point point, string era, ulong slot, ulong blockNum) = ExtractPoint(rollForward);
                        headerBatch.Add((point, era, slot, blockNum));
                        break;

                    case MessageRollBackward:
                        break;

                    case MessageAwaitReply:
                        Console.WriteLine("Reached chain tip.");
                        goto done;

                    default:
                        break;
                }
            }

            for (int i = 0; i < headerBatch.Count; i++)
            {
                try
                {
                    BlockWithEra block = await blockFetch!.FetchSingleAsync<BlockWithEra>(headerBatch[i].Point, cts.Token).ConfigureAwait(false);

                    totalBlocksSynced++;
                    windowBlocks++;
                    int blockLen = block.Raw.Length;
                    totalBytesDownloaded += blockLen;
                    windowBytes += blockLen;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"WARN: block {headerBatch[i].Era} slot {headerBatch[i].Slot}: {ex.Message}");
                    continue;
                }

                lastEra = headerBatch[i].Era;
                lastSlot = headerBatch[i].Slot;
                lastBlockNumber = headerBatch[i].BlockNum;

                if (totalBlocksSynced % ReportInterval == 0)
                {
                    PrintProgress(totalTimer, windowTimer, lastSlot, lastBlockNumber, lastEra,
                        windowBlocks, windowBytes, totalBytesDownloaded);
                    windowTimer.Restart();
                    windowBlocks = 0;
                    windowBytes = 0;
                }
            }
        }

    done:

        totalTimer.Stop();
        double totalSeconds = totalTimer.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"  Blocks synced:  {totalBlocksSynced}");
        Console.WriteLine($"  Last slot:      {lastSlot}");
        Console.WriteLine($"  Last era:       {lastEra}");
        Console.WriteLine($"  Total time:     {totalSeconds:F1}s");
        Console.WriteLine($"  Avg blocks/s:   {totalBlocksSynced / totalSeconds:F1}");
        Console.WriteLine($"  Avg throughput: {FormatBytes(totalBytesDownloaded / totalSeconds)}/s");
        Console.WriteLine($"  Total data:     {FormatBytes(totalBytesDownloaded)}");
        Console.WriteLine($"  Deser errors:   {deserErrors}");
        if (errorBuckets.Count > 0)
        {
            Console.WriteLine("  Error breakdown:");
            foreach (KeyValuePair<string, int> kv in errorBuckets.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"    {kv.Value,5}x {kv.Key}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        totalTimer.Stop();
        Console.WriteLine();
        Console.WriteLine($"Cancelled after {totalBlocksSynced} blocks in {totalTimer.Elapsed.TotalSeconds:F1}s");
    }

    // Cleanup
    if (chainSync.HasAgency)
    {
        try { await chainSync.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (InvalidOperationException) { }
    }

    if (blockFetch is { HasAgency: true })
    {
        try { await blockFetch.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (InvalidOperationException) { }
    }
}

return 0;

// ---

static void PrintProgress(Stopwatch totalTimer, Stopwatch windowTimer,
    ulong slot, ulong blockNum, string era,
    int windowBlocks, long windowBytes, long totalBytes)
{
    double windowElapsed = windowTimer.Elapsed.TotalSeconds;
    double windowBlkPerSec = windowBlocks / windowElapsed;
    double windowBytesPerSec = windowBytes / windowElapsed;

    Console.WriteLine(
        $"[{totalTimer.Elapsed:hh\\:mm\\:ss}] slot {slot,10} block {blockNum,8} [{era,-10}] | " +
        $"{windowBlkPerSec,7:F1} blk/s | {FormatBytes(windowBytesPerSec)}/s | {FormatBytes(totalBytes)} total");
}

static (Point Point, string Era, ulong Slot, ulong BlockNum) ExtractPoint(MessageRollForward rollForward)
{
    ChainSyncHeader header = ChainSyncHeader.Decode(rollForward.Payload.Value);
    ChainPoint chainPoint = header.ExtractPoint();
    Point point = Point.Specific(chainPoint.Slot, chainPoint.Hash);
    return (point, header.Era, chainPoint.Slot, chainPoint.BlockNumber);
}

static string FormatBytes(double bytes)
{
    return bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576:F2} MB",
        >= 1024 => $"{bytes / 1024:F1} KB",
        _ => $"{bytes:F0} B"
    };
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string key = args[i][2..];
        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

        if (hasValue)
        {
            map[key] = args[i + 1];
            i++;
            continue;
        }

        // Support boolean flags like --no-deser without a value
        map[key] = string.Empty;
    }
    return map;
}

static string GetArg(Dictionary<string, string> map, string key, string fallback)
{
    return map.TryGetValue(key, out string? value) ? value : fallback;
}

static string? GetArgOrNull(Dictionary<string, string> map, string key)
{
    return map.TryGetValue(key, out string? value) ? value : null;
}
