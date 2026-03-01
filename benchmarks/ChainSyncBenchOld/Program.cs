using System.Diagnostics;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

const ulong DefaultMagic = 2;
const int DefaultBlockCount = 10000;
const int ReportInterval = 1000;

Dictionary<string, string> args_ = ParseArgs(args);
ulong magic = ulong.Parse(GetArg(args_, "magic", DefaultMagic.ToString()));
int targetBlocks = int.Parse(GetArg(args_, "blocks", DefaultBlockCount.ToString()));
string? socketPath = GetArgOrNull(args_, "socket");
string? fromSlotStr = GetArgOrNull(args_, "slot");
string? fromHash = GetArgOrNull(args_, "hash");
bool noDeser = GetArgOrNull(args_, "no-deser") is not null;

if (socketPath is null || fromSlotStr is null || fromHash is null)
{
    Console.Error.WriteLine("Usage: --socket <path> --slot <n> --hash <hex> [--magic <n>] [--blocks <n>]");
    return 1;
}

Console.WriteLine($"ChainSync Benchmark OLD (N2C) - Chrysalis 1.1.0-alpha");
Console.WriteLine($"  Socket:     {socketPath}");
Console.WriteLine($"  Magic:      {magic}");
Console.WriteLine($"  Target:     {targetBlocks} blocks");
Console.WriteLine();

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

NodeClient node = await NodeClient.ConnectAsync(socketPath, cts.Token);
await node.StartAsync(magic);
ChainSync chainSync = node.ChainSync;

using (node)
{
    List<Point> startPoints = [new Point(ulong.Parse(fromSlotStr), Convert.FromHexString(fromHash))];
    Console.WriteLine($"  Starting from slot {fromSlotStr}");

    Console.WriteLine("Connected. Starting sync...");
    Console.WriteLine();

    ChainSyncMessage intersect = await chainSync.FindIntersectionAsync(startPoints, cts.Token);
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

    try
    {
        while (totalBlocksSynced < targetBlocks && !cts.Token.IsCancellationRequested)
        {
            MessageNextResponse? response = await chainSync.NextRequestAsync(cts.Token);

            switch (response)
            {
                case MessageRollForward rollForward:
                    int payloadLen = rollForward.Payload.Value.Length;

                    if (!noDeser)
                    {
                        try { CborSerializer.Deserialize<BlockWithEra>(rollForward.Payload.GetValue()); } catch { }
                    }

                    totalBlocksSynced++;
                    windowBlocks++;
                    totalBytesDownloaded += payloadLen;
                    windowBytes += payloadLen;

                    if (totalBlocksSynced % ReportInterval == 0)
                    {
                        PrintProgress(totalTimer, windowTimer,
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
            }
        }

        done:

        totalTimer.Stop();
        double totalSeconds = totalTimer.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"  Blocks synced:  {totalBlocksSynced}");
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

    if (chainSync.HasAgency)
    {
        try { await chainSync.DoneAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
    }
}

return 0;

static void PrintProgress(Stopwatch totalTimer, Stopwatch windowTimer,
    int windowBlocks, long windowBytes, long totalBytes)
{
    double windowElapsed = windowTimer.Elapsed.TotalSeconds;
    double windowBlkPerSec = windowBlocks / windowElapsed;
    double windowBytesPerSec = windowBytes / windowElapsed;

    Console.WriteLine(
        $"[{totalTimer.Elapsed:hh\\:mm\\:ss}] | " +
        $"{windowBlkPerSec,7:F1} blk/s | {FormatBytes(windowBytesPerSec)}/s | {FormatBytes(totalBytes)} total");
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
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        string key = args[i][2..];
        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

        if (hasValue)
        {
            map[key] = args[i + 1];
            i++;
        }
        else
        {
            map[key] = string.Empty;
        }
    }
    return map;
}

static string GetArg(Dictionary<string, string> map, string key, string fallback)
    => map.TryGetValue(key, out string? value) ? value : fallback;

static string? GetArgOrNull(Dictionary<string, string> map, string key)
    => map.TryGetValue(key, out string? value) ? value : null;
