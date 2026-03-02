using System.Diagnostics;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Byron;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

internal static class Program
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3001;
    private const ulong DefaultNetworkMagic = 2;
    private const int DefaultKeepAliveSeconds = 20;
    private const int DefaultBlockCount = 100;
    private const int DefaultPipelineDepth = 50;

    private sealed record Options(
        string Command,
        string? Socket,
        string Host,
        int Port,
        ulong NetworkMagic,
        int KeepAliveSeconds,
        int BlockCount,
        int PipelineDepth,
        ulong? Slot,
        string? Hash
    );

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out Options options, out string? error))
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            }

            PrintUsage();
            return 1;
        }

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (options.Socket is not null)
        {
            Console.WriteLine($"Connecting to {options.Socket} (N2C, magic {options.NetworkMagic})...");

            using NodeClient node = await NodeClient.ConnectAsync(options.Socket, cts.Token).ConfigureAwait(false);
            await node.StartAsync(options.NetworkMagic).ConfigureAwait(false);

            Console.WriteLine("Connected.");

            return options.Command switch
            {
                "CHAINSYNC" => await RunChainSyncN2C(node.ChainSync, cts.Token).ConfigureAwait(false),
                "BLOCKFETCH" => throw new InvalidOperationException("BlockFetch is not available over N2C. Use N2N (--tcp-host/--tcp-port) instead."),
                _ => throw new InvalidOperationException($"Unknown command: {options.Command}")
            };
        }

        Console.WriteLine($"Connecting to {options.Host}:{options.Port} (N2N, magic {options.NetworkMagic})...");

        using PeerClient peer = await PeerClient.ConnectAsync(options.Host, options.Port, cts.Token).ConfigureAwait(false);
        await peer.StartAsync(options.NetworkMagic, TimeSpan.FromSeconds(options.KeepAliveSeconds)).ConfigureAwait(false);

        Console.WriteLine("Connected.");

        return options.Command switch
        {
            "CHAINSYNC" => await RunChainSyncN2N(peer, options.PipelineDepth, cts.Token).ConfigureAwait(false),
            "BLOCKFETCH" => await RunBlockFetch(peer, options, cts.Token).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown command: {options.Command}")
        };
    }

    #region ChainSync N2C

    private static async Task<int> RunChainSyncN2C(ChainSync chainSync, CancellationToken ct)
    {
        Console.WriteLine("Starting ChainSync from origin (N2C)...");

        ChainSyncMessage intersect = await chainSync.FindIntersectionAsync([Point.Origin], ct).ConfigureAwait(false);
        if (intersect is not MessageIntersectFound found)
        {
            Console.WriteLine("Failed to find intersection.");
            return 2;
        }
        Console.WriteLine($"Intersection found at {FormatPoint(found.Point)}");

        Stopwatch totalTimer = Stopwatch.StartNew();
        Stopwatch windowTimer = Stopwatch.StartNew();
        long totalBlocks = 0;
        int windowBlocks = 0;
        Era lastEra = Era.Byron;
        ulong lastSlot = 0;
        ulong lastBlockNumber = 0;
        Point tipPoint = Point.Origin;
        bool atTip = false;

        while (!ct.IsCancellationRequested)
        {
            MessageNextResponse? response = await chainSync.NextRequestAsync(ct).ConfigureAwait(false);

            switch (response)
            {
                case MessageRollForward rollForward:
                    atTip = false;
                    totalBlocks++;
                    windowBlocks++;
                    tipPoint = rollForward.Tip.Slot;
                    ExtractBlockInfo(rollForward, ref lastEra, ref lastSlot, ref lastBlockNumber);

                    if (windowTimer.Elapsed.TotalSeconds >= 3)
                    {
                        PrintProgress(totalTimer, windowTimer, totalBlocks, windowBlocks, lastSlot, lastBlockNumber, lastEra, tipPoint);
                        windowTimer.Restart();
                        windowBlocks = 0;
                    }
                    break;

                case MessageRollBackward rollBackward:
                    atTip = false;
                    Console.WriteLine($"rollback to {FormatPoint(rollBackward.Point)}");
                    break;

                case MessageAwaitReply:
                    if (!atTip)
                    {
                        PrintAtTip(totalTimer, totalBlocks, tipPoint);
                        atTip = true;
                    }
                    break;
                default:
                    break;
            }
        }

        PrintStopped(totalTimer, totalBlocks);
        await TryDoneAsync(chainSync).ConfigureAwait(false);
        return 0;
    }

    #endregion

    #region ChainSync N2N

    private static async Task<int> RunChainSyncN2N(PeerClient peer, int pipelineDepth, CancellationToken ct)
    {
        Console.WriteLine($"Starting pipelined ChainSync from origin (N2N, pipeline depth {pipelineDepth})...");

        ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct).ConfigureAwait(false);
        if (intersect is not MessageIntersectFound found)
        {
            Console.WriteLine("Failed to find intersection.");
            return 2;
        }
        Console.WriteLine($"Intersection found at {FormatPoint(found.Point)}");

        Stopwatch totalTimer = Stopwatch.StartNew();
        Stopwatch windowTimer = Stopwatch.StartNew();
        long totalBlocks = 0;
        int windowBlocks = 0;
        Era lastEra = Era.Byron;
        ulong lastSlot = 0;
        ulong lastBlockNumber = 0;
        Point tipPoint = Point.Origin;

        while (!ct.IsCancellationRequested)
        {
            // Phase 1: Send a burst of pipelined NextRequest messages
            int toSend = pipelineDepth;
            await peer.ChainSync.SendNextRequestBatchAsync(toSend, ct).ConfigureAwait(false);

            // Phase 2: Drain all responses and collect header points
            List<Point> batch = [];
            bool reachedTip = false;
            int received = 0;

            while (received < toSend && !ct.IsCancellationRequested)
            {
                MessageNextResponse response = await peer.ChainSync.ReceiveNextResponseAsync(ct).ConfigureAwait(false);
                received++;

                switch (response)
                {
                    case MessageRollForward rollForward:
                        tipPoint = rollForward.Tip.Slot;
                        ExtractHeaderPoint(rollForward, out Point? headerPoint);
                        if (headerPoint is not null)
                        {
                            batch.Add(headerPoint);
                        }
                        else
                        {
                            Console.WriteLine($"  WARN: skipped unparseable header (payload {rollForward.Payload.Value.Length} bytes)");
                        }
                        break;
                    case MessageRollBackward rollBackward:
                        Console.WriteLine($"rollback to {FormatPoint(rollBackward.Point)}");
                        batch.Clear();
                        break;
                    case MessageAwaitReply:
                        reachedTip = true;
                        break;
                    default:
                        break;
                }

                if (reachedTip)
                {
                    break;
                }
            }

            // Phase 3: BlockFetch the collected headers
            if (batch.Count > 0)
            {
                await peer.BlockFetch.RequestRangeAsync(batch[0], batch[^1], ct).ConfigureAwait(false);
                await foreach (BlockFetchMessage msg in peer.BlockFetch.ReceiveBlockMessagesAsync(ct).ConfigureAwait(false))
                {
                    if (msg is not BlockBody blockBody)
                    {
                        continue;
                    }

                    totalBlocks++;
                    windowBlocks++;

                    try
                    {
                        BlockWithEra block = blockBody.Body.Deserialize<BlockWithEra>();
                        if (block.Block is not null)
                        {
                            lastEra = block.Era();
                            lastSlot = block.Block.Slot();
                            lastBlockNumber = block.Block.Height();
                        }
                    }
                    catch
                    {
                        // Skip blocks that fail to deserialize but keep streaming
                    }

                    if (windowTimer.Elapsed.TotalSeconds >= 3)
                    {
                        PrintProgress(totalTimer, windowTimer, totalBlocks, windowBlocks, lastSlot, lastBlockNumber, lastEra, tipPoint);
                        windowTimer.Restart();
                        windowBlocks = 0;
                    }
                }
            }

            if (reachedTip)
            {
                PrintAtTip(totalTimer, totalBlocks, tipPoint);
                Console.WriteLine("awaiting new blocks...");

                // Fall back to sequential mode at the tip
                while (!ct.IsCancellationRequested)
                {
                    MessageNextResponse? response = await peer.ChainSync.NextRequestAsync(ct).ConfigureAwait(false);
                    if (response is MessageRollForward)
                    {
                        break;
                    }
                }
            }
        }

        PrintStopped(totalTimer, totalBlocks);
        await TryDoneAsync(peer.ChainSync).ConfigureAwait(false);

        if (peer.BlockFetch.HasAgency)
        {
            try { await peer.BlockFetch.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Best-effort shutdown */ }
        }

        return 0;
    }

    #endregion

    #region BlockFetch

    private static async Task<int> RunBlockFetch(PeerClient peer, Options options, CancellationToken ct)
    {
        Point startPoint = (options.Slot is not null && options.Hash is not null)
            ? Point.Specific(options.Slot.Value, Convert.FromHexString(options.Hash))
            : Point.Origin;

        Console.WriteLine($"BlockFetch: syncing headers from {FormatPoint(startPoint)}, then fetching {options.BlockCount} blocks...");

        ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([startPoint], ct).ConfigureAwait(false);
        if (intersect is not MessageIntersectFound)
        {
            Console.WriteLine("Failed to find intersection.");
            return 2;
        }

        // Collect header points via ChainSync
        List<Point> points = [];
        while (points.Count < options.BlockCount && !ct.IsCancellationRequested)
        {
            MessageNextResponse? response = await peer.ChainSync.NextRequestAsync(ct).ConfigureAwait(false);
            switch (response)
            {
                case MessageRollForward rollForward:
                    ExtractHeaderPoint(rollForward, out Point? headerPoint);
                    if (headerPoint is not null)
                    {
                        points.Add(headerPoint);
                    }
                    break;
                case MessageRollBackward:
                    break;
                case MessageAwaitReply:
                    Console.WriteLine("Reached chain tip during header collection.");
                    goto fetchBlocks;
                default:
                    break;
            }
        }

    fetchBlocks:
        if (points.Count == 0)
        {
            Console.WriteLine("No headers collected.");
            return 2;
        }

        Console.WriteLine($"Collected {points.Count} headers. Fetching blocks...");

        Stopwatch timer = Stopwatch.StartNew();
        Stopwatch windowTimer = Stopwatch.StartNew();
        int totalFetched = 0;
        int windowFetched = 0;
        Era lastEra = Era.Byron;
        ulong lastSlot = 0;
        ulong lastHeight = 0;

        // Fetch in batches using FetchRangeAsync (start..end of each batch)
        int batchSize = Math.Min(100, points.Count);
        for (int i = 0; i < points.Count && !ct.IsCancellationRequested; i += batchSize)
        {
            int end = Math.Min(i + batchSize - 1, points.Count - 1);
            await foreach (BlockWithEra block in peer.BlockFetch.FetchRangeAsync<BlockWithEra>(points[i], points[end], ct).ConfigureAwait(false))
            {
                totalFetched++;
                windowFetched++;

                if (block.Block is not null)
                {
                    lastEra = block.Era();
                    lastSlot = block.Block.Slot();
                    lastHeight = block.Block.Height();
                }

                if (windowTimer.Elapsed.TotalSeconds >= 3)
                {
                    double blkPerSec = windowFetched / windowTimer.Elapsed.TotalSeconds;
                    Console.WriteLine(
                        $"[{timer.Elapsed:hh\\:mm\\:ss}] slot {lastSlot,10} block {lastHeight,8} [{lastEra,-10}] | " +
                        $"{blkPerSec,7:F1} blk/s | {totalFetched}/{points.Count} fetched");
                    windowTimer.Restart();
                    windowFetched = 0;
                }
            }
        }

        timer.Stop();
        Console.WriteLine();
        Console.WriteLine($"Fetched {totalFetched} blocks in {timer.Elapsed.TotalSeconds:F1}s ({totalFetched / timer.Elapsed.TotalSeconds:F1} blk/s)");
        Console.WriteLine($"Last: slot {lastSlot} block {lastHeight} [{lastEra}]");

        if (peer.BlockFetch.HasAgency)
        {
            try { await peer.BlockFetch.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Best-effort shutdown */ }
        }

        return 0;
    }

    private static void ExtractHeaderPoint(MessageRollForward rollForward, out Point? point)
    {
        point = null;
        try
        {
            HeaderContent header = HeaderContent.Decode(rollForward.Payload.Value);

            if (header.IsByron)
            {
                if (header.IsByronEbb)
                {
                    ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(header.HeaderCbor);
                    ulong slot = ebbHead.ConsensusData.EpochId * 21600;
                    byte[] hash = HashByronHeader(0, header.HeaderCbor.Span);
                    point = Point.Specific(slot, hash);
                }
                else
                {
                    ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(header.HeaderCbor);
                    ulong slot = (blockHead.ConsensusData.SlotId.Epoch * 21600) + blockHead.ConsensusData.SlotId.Slot;
                    byte[] hash = HashByronHeader(1, header.HeaderCbor.Span);
                    point = Point.Specific(slot, hash);
                }
                return;
            }

            BlockHeader blockHeader = CborSerializer.Deserialize<BlockHeader>(header.HeaderCbor);
            ulong headerSlot = blockHeader.HeaderBody.Slot();
            byte[] headerHash = Convert.FromHexString(blockHeader.Hash());
            point = Point.Specific(headerSlot, headerHash);
        }
        catch
        {
            // skip unparseable headers
        }
    }

    /// <summary>
    /// Computes the Byron block hash by wrapping the header in a CBOR tuple [tag, header_bytes] before hashing.
    /// Byron EBB uses tag=0, Byron main block uses tag=1.
    /// </summary>
    private static byte[] HashByronHeader(byte tag, ReadOnlySpan<byte> headerCbor)
    {
        byte[] wrapped = new byte[2 + headerCbor.Length];
        wrapped[0] = 0x82; // CBOR array(2)
        wrapped[1] = tag;  // CBOR uint(0) or uint(1)
        headerCbor.CopyTo(wrapped.AsSpan(2));
        return Blake2Fast.Blake2b.HashData(32, wrapped);
    }

    #endregion

    #region Formatting

    private static void ExtractBlockInfo(MessageRollForward rollForward, ref Era era, ref ulong slot, ref ulong blockNumber)
    {
        try
        {
            BlockWithEra block = rollForward.Payload.Deserialize<BlockWithEra>();
            if (block.Block is null)
            {
                return;
            }
            era = block.Era();
            slot = block.Block.Slot();
            blockNumber = block.Block.Height();
        }
        catch
        {
            // keep previous era value
        }
    }

    private static void PrintProgress(Stopwatch totalTimer, Stopwatch windowTimer, long totalBlocks, int windowBlocks,
        ulong slot, ulong blockNumber, Era era, Point tipPoint)
    {
        double blkPerSec = windowBlocks / windowTimer.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"[{totalTimer.Elapsed:hh\\:mm\\:ss}] slot {slot,10} block {blockNumber,8} [{era,-10}] | " +
            $"{blkPerSec,7:F1} blk/s | {totalBlocks} total | tip {FormatPoint(tipPoint)}");
    }

    private static void PrintAtTip(Stopwatch totalTimer, long totalBlocks, Point tipPoint)
    {
        double totalSec = totalTimer.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"[{totalTimer.Elapsed:hh\\:mm\\:ss}] at tip | {totalBlocks} blocks synced | " +
            $"{totalBlocks / totalSec:F1} avg blk/s | tip {FormatPoint(tipPoint)}");
    }

    private static void PrintStopped(Stopwatch totalTimer, long totalBlocks)
    {
        totalTimer.Stop();
        Console.WriteLine();
        Console.WriteLine($"Stopped after {totalBlocks} blocks in {totalTimer.Elapsed.TotalSeconds:F1}s ({totalBlocks / totalTimer.Elapsed.TotalSeconds:F1} avg blk/s)");
    }

    private static async Task TryDoneAsync(ChainSync chainSync)
    {
        if (chainSync.HasAgency)
        {
            try { await chainSync.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Best-effort shutdown */ }
        }
    }

    private static string FormatPoint(Point point)
    {
        return point switch
        {
            SpecificPoint sp => $"slot {sp.Slot} hash {Convert.ToHexStringLower(sp.Hash.Span)}",
            _ => "origin"
        };
    }

    #endregion

    #region Options parsing

    private static bool TryParseOptions(string[] args, out Options options, out string? error)
    {
        options = default!;
        error = null;

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // First positional arg is the command (default: chainsync)
        string command = "chainsync";
        int startIdx = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = args[0].ToUpperInvariant();
            startIdx = 1;
        }

        if (command is not "CHAINSYNC" and not "BLOCKFETCH")
        {
            error = $"Unknown command '{command}'. Use 'CHAINSYNC' or 'BLOCKFETCH'.";
            return false;
        }

        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        for (int i = startIdx; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            string key = arg[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "Invalid argument format.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Missing value for '{arg}'.";
                return false;
            }

            map[key] = args[i + 1];
            i++;
        }

        string? socket = GetValue(map, "socket") ?? GetEnv("SOCKET");
        string host = GetValue(map, "tcp-host") ?? GetEnv("TcpHost", "HOST") ?? DefaultHost;
        int port = ParseInt(GetValue(map, "tcp-port") ?? GetEnv("TcpPort", "PORT"), DefaultPort, "port", ref error);
        ulong magic = ParseUlong(GetValue(map, "magic") ?? GetEnv("NetworkMagic", "NETWORK_MAGIC"), DefaultNetworkMagic, "network magic", ref error);
        int keepAlive = ParseInt(
            GetValue(map, "keepalive") ?? GetEnv("KeepAliveIntervalSeconds", "KEEPALIVE_INTERVAL_SECONDS"),
            DefaultKeepAliveSeconds, "keepalive seconds", ref error);
        int blockCount = ParseInt(
            GetValue(map, "blocks") ?? GetEnv("BLOCKS"),
            DefaultBlockCount, "block count", ref error);
        int pipelineDepth = ParseInt(GetValue(map, "pipeline"), DefaultPipelineDepth, "pipeline depth", ref error);

        ulong? slot = null;
        string? hash = null;

        if (map.TryGetValue("slot", out string? slotStr))
        {
            if (!ulong.TryParse(slotStr, out ulong parsedSlot))
            {
                error = $"Invalid slot value '{slotStr}'.";
                return false;
            }
            slot = parsedSlot;
        }

        hash = GetValue(map, "hash");

        if (!string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        options = new Options(command, socket, host, port, magic, keepAlive, blockCount, pipelineDepth, slot, hash);
        return true;
    }

    private static string? GetValue(Dictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out string? value) ? value : null;
    }

    private static string? GetEnv(params string[] names)
    {
        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static int ParseInt(string? value, int fallback, string label, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out int parsed))
        {
            error = $"Invalid {label} value '{value}'.";
            return fallback;
        }
        return parsed;
    }

    private static ulong ParseUlong(string? value, ulong fallback, string label, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!ulong.TryParse(value, out ulong parsed))
        {
            error = $"Invalid {label} value '{value}'.";
            return fallback;
        }
        return parsed;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/Chrysalis.Network.Cli -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  chainsync    Sync headers + fetch blocks from origin (default)");
        Console.WriteLine("  blockfetch   Fetch N blocks via ChainSync + BlockFetch (N2N only)");
        Console.WriteLine();
        Console.WriteLine("Connection (pick one):");
        Console.WriteLine("  --socket <path>              N2C Unix socket path (SOCKET env)");
        Console.WriteLine("  --tcp-host <host>            N2N TCP host (TcpHost / HOST, default 127.0.0.1)");
        Console.WriteLine("  --tcp-port <port>            N2N TCP port (TcpPort / PORT, default 3001)");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --magic <magic>              NetworkMagic / NETWORK_MAGIC (default 2)");
        Console.WriteLine("  --keepalive <seconds>        KeepAliveIntervalSeconds (default 20, N2N only)");
        Console.WriteLine("  --slot <slot>                Start slot (default: origin)");
        Console.WriteLine("  --hash <hex>                 Start block hash (hex-encoded)");
        Console.WriteLine("  --blocks <count>             Number of blocks to fetch (default 100, blockfetch only)");
        Console.WriteLine("  --pipeline <depth>           Pipeline depth for N2N chainsync (default 50)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- chainsync --socket /path/to/node.socket");
        Console.WriteLine("  dotnet run -- chainsync --tcp-host 127.0.0.1 --tcp-port 3001");
        Console.WriteLine("  dotnet run -- blockfetch --blocks 500 --slot 12345 --hash abcdef...");
    }

    #endregion
}
