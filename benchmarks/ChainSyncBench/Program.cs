using System.Diagnostics;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Byron;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
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
int port = int.Parse(GetArg(args_, "tcp-port", DefaultPort.ToString()));
ulong magic = ulong.Parse(GetArg(args_, "magic", DefaultMagic.ToString()));
int targetBlocks = int.Parse(GetArg(args_, "blocks", DefaultBlockCount.ToString()));
int batchSize = int.Parse(GetArg(args_, "batch", DefaultBatchSize.ToString()));
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
    NodeClient node = await NodeClient.ConnectAsync(socketPath!, cts.Token);
    await node.StartAsync(magic);
    chainSync = node.ChainSync;
    client = node;
}
else
{
    PeerClient peer = await PeerClient.ConnectAsync(host, port, cts.Token);
    await peer.StartAsync(magic);
    chainSync = peer.ChainSync;
    blockFetch = peer.BlockFetch;
    client = peer;
}

using (client)
{
    Point startPoint = (fromSlotStr is not null && fromHash is not null)
        ? Point.Specific(ulong.Parse(fromSlotStr), Convert.FromHexString(fromHash))
        : Point.Origin;
    if (startPoint != Point.Origin && useUnixSocket)
    {
        Console.WriteLine($"  Starting from slot {fromSlotStr}");
        Console.WriteLine();
    }

    Console.WriteLine("Connected. Starting sync...");
    Console.WriteLine();

    ChainSyncMessage intersect = await chainSync.FindIntersectionAsync([startPoint], cts.Token);
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
    string lastEra = useUnixSocket ? "N2C" : "?";
    ulong lastSlot = 0;
    ulong lastBlockNumber = 0;

    try
    {
        while (totalBlocksSynced < targetBlocks && !cts.Token.IsCancellationRequested)
        {
            if (useUnixSocket)
            {
                // N2C mode: each RollForward payload IS the full block
                MessageNextResponse? response = await chainSync.NextRequestAsync(cts.Token);

                switch (response)
                {
                    case MessageRollForward rollForward:
                        int payloadLen = rollForward.Payload.Value.Length;

                        if (!noDeser)
                        {
                            try
                            {
                                BlockWithEra block = rollForward.Payload.DeserializeWithoutRaw<BlockWithEra>();

                                if (TryGetBlockProgressInfo(block, out ulong slot, out ulong blockNumber))
                                {
                                    lastSlot = slot;
                                    lastBlockNumber = blockNumber;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"WARN: block decode failed ({payloadLen} bytes): {ex.Message}");
                            }
                        }

                        if (noDeser)
                        {
                            lastSlot = 0;
                            lastBlockNumber = 0;
                            lastEra = "N2C";
                        }
                        else
                        {
                            lastEra = "N2C";
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
                MessageNextResponse? response = await chainSync.NextRequestAsync(cts.Token);

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
                    BlockWithEra? block = await blockFetch!.FetchSingleAsync<BlockWithEra>(headerBatch[i].Point, cts.Token);
                    if (block is null) continue;

                    totalBlocksSynced++;
                    windowBlocks++;
                    int blockLen = block.Raw?.Length ?? 0;
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
        try { await chainSync.DoneAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
    }

    if (blockFetch is { HasAgency: true })
    {
        try { await blockFetch.DoneAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
    }
}

return 0;

// ---

static bool TryGetBlockProgressInfo(BlockWithEra blockWithEra, out ulong slot, out ulong blockNumber)
{
    slot = 0;
    blockNumber = 0;

    switch (blockWithEra.Block)
    {
        case ByronEbBlock byronEbbBlock:
            slot = byronEbbBlock.Epoch() * 21600;
            blockNumber = 0;
            return true;

        case ByronMainBlock byronMainBlock:
            slot = (byronMainBlock.Epoch() * 21600) + byronMainBlock.Slot();
            blockNumber = byronMainBlock.Header.ConsensusData.Difficulty.GetValue().FirstOrDefault();
            return true;

        case AlonzoCompatibleBlock:
        case BabbageBlock:
        case ConwayBlock:
            BlockHeader header = blockWithEra.Block.Header();
            slot = header.HeaderBody.Slot();
            blockNumber = header.HeaderBody.BlockNumber();
            return true;

        default:
            return false;
    }
}

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
    HeaderContent header = HeaderContent.Decode(rollForward.Payload.Value);

    if (header.IsByron)
    {
        return ExtractByronPoint(header);
    }

    BlockHeader blockHeader = CborSerializer.Deserialize<BlockHeader>(header.HeaderCbor);
    ulong slot = blockHeader.HeaderBody.Slot();
    ulong blockNumber = blockHeader.HeaderBody.BlockNumber();
    byte[] hash = Convert.FromHexString(blockHeader.Hash());
    Point point = Point.Specific(slot, hash);
    return (point, header.Era, slot, blockNumber);
}

static (Point Point, string Era, ulong Slot, ulong BlockNum) ExtractByronPoint(HeaderContent header)
{
    byte[] hash = Blake2Fast.Blake2b.HashData(32, header.HeaderCbor.Span);

    if (header.IsByronEbb)
    {
        ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(header.HeaderCbor);
        ulong ebbSlot = ebbHead.ConsensusData.EpochId * 21600;
        Point point = Point.Specific(ebbSlot, hash);
        return (point, header.Era, ebbSlot, 0);
    }

    ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(header.HeaderCbor);
    ulong epoch = blockHead.ConsensusData.SlotId.Epoch;
    ulong relSlot = blockHead.ConsensusData.SlotId.Slot;
    ulong absSlot = (epoch * 21600) + relSlot;
    ulong blockNumber = blockHead.ConsensusData.Difficulty.GetValue().FirstOrDefault();
    Point point2 = Point.Specific(absSlot, hash);
    return (point2, header.Era, absSlot, blockNumber);
}

static string FormatBytes(double bytes) => bytes switch
{
    >= 1_073_741_824 => $"{bytes / 1_073_741_824:F2} GB",
    >= 1_048_576 => $"{bytes / 1_048_576:F2} MB",
    >= 1024 => $"{bytes / 1024:F1} KB",
    _ => $"{bytes:F0} B"
};

static Dictionary<string, string> ParseArgs(string[] args)
{
    Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
        {
            continue;
        }

        string key = args[i][2..];
        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");

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
    => map.TryGetValue(key, out string? value) ? value : fallback;

static string? GetArgOrNull(Dictionary<string, string> map, string key)
    => map.TryGetValue(key, out string? value) ? value : null;
