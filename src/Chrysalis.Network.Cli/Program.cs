using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;

internal static class Program
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3001;
    private const ulong DefaultNetworkMagic = 2;
    private const int DefaultKeepAliveSeconds = 20;

    private sealed record Options(
        string Command,
        string Host,
        int Port,
        ulong NetworkMagic,
        int KeepAliveSeconds,
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

        Console.WriteLine($"Connecting to {options.Host}:{options.Port} (magic {options.NetworkMagic})...");

        using PeerClient peer = await PeerClient.ConnectAsync(options.Host, options.Port, cts.Token).ConfigureAwait(false);
        await peer.StartAsync(options.NetworkMagic, TimeSpan.FromSeconds(options.KeepAliveSeconds)).ConfigureAwait(false);

        Console.WriteLine("Connected.");

        return options.Command switch
        {
            "CHAINSYNC" => await RunChainSync(peer, cts.Token).ConfigureAwait(false),
            "BLOCKFETCH" => await RunBlockFetch(peer, options, cts.Token).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown command: {options.Command}")
        };
    }

    #region ChainSync

    private static async Task<int> RunChainSync(PeerClient peer, CancellationToken ct)
    {
        Console.WriteLine("Starting ChainSync from origin...");

        ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct).ConfigureAwait(false);

        switch (intersect)
        {
            case MessageIntersectFound found:
                Console.WriteLine($"Intersection found at {FormatPoint(found.Point)}");
                break;
            case MessageIntersectNotFound notFound:
                Console.WriteLine($"Intersection not found. Tip {FormatPoint(notFound.Tip.Slot)}");
                return 2;
            default:
                Console.WriteLine("Unexpected intersection response.");
                return 2;
        }

        bool atTip = false;

        while (!ct.IsCancellationRequested)
        {
            MessageNextResponse? response = await peer.ChainSync.NextRequestAsync(ct).ConfigureAwait(false);

            switch (response)
            {
                case MessageRollForward rollForward:
                    atTip = false;
                    Console.WriteLine(FormatRollForward(rollForward));
                    break;

                case MessageRollBackward rollBackward:
                    atTip = false;
                    Console.WriteLine($"rollback to {FormatPoint(rollBackward.Point)}");
                    break;

                case MessageAwaitReply:
                    if (!atTip)
                    {
                        Console.WriteLine("await");
                        atTip = true;
                    }
                    break;
                default:
                    break;
            }
        }

        if (peer.ChainSync.HasAgency)
        {
            try { await peer.ChainSync.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Best-effort shutdown */ }
        }

        return 0;
    }

    #endregion

    #region BlockFetch

    private static async Task<int> RunBlockFetch(PeerClient peer, Options options, CancellationToken ct)
    {
        Point point;

        if (options.Slot is not null && options.Hash is not null)
        {
            byte[] hashBytes = Convert.FromHexString(options.Hash);
            point = Point.Specific(options.Slot.Value, hashBytes);
        }
        else
        {
            // Use chainsync to discover a block point first
            Console.WriteLine("No --slot/--hash given, using ChainSync to discover a block...");
            ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct).ConfigureAwait(false);

            if (intersect is not MessageIntersectFound)
            {
                Console.WriteLine("Failed to find intersection.");
                return 2;
            }

            // Get first rollforward to have a real block point
            MessageNextResponse? response = await peer.ChainSync.NextRequestAsync(ct).ConfigureAwait(false);
            while (response is MessageRollBackward or MessageAwaitReply)
            {
                response = await peer.ChainSync.NextRequestAsync(ct).ConfigureAwait(false);
            }

            if (response is not MessageRollForward rollForward)
            {
                Console.WriteLine("Failed to get a block from ChainSync.");
                return 2;
            }

            // Extract the tip point as our fetch target (we know this block exists)
            point = rollForward.Tip.Slot;
            Console.WriteLine($"Discovered block: {FormatPoint(point)}");
        }

        Console.WriteLine($"Fetching block at {FormatPoint(point)}...");

        BlockWithEra? block = await peer.BlockFetch.FetchSingleAsync<BlockWithEra>(point, ct).ConfigureAwait(false);

        if (block is null)
        {
            Console.WriteLine("NoBlocks — block not found.");
            return 2;
        }

        Console.WriteLine($"Deserialized: {block.GetType().Name}");

        Block? inner = block.Block;
        if (inner is not null)
        {
            Console.WriteLine($"Era block type: {inner.GetType().Name}");
        }

        if (peer.BlockFetch.HasAgency)
        {
            try { await peer.BlockFetch.DoneAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Best-effort shutdown */ }
        }

        return 0;
    }

    #endregion

    #region Formatting

    private static string FormatRollForward(MessageRollForward rollForward)
    {
        try
        {
            HeaderContent header = HeaderContent.Decode(rollForward.Payload.Value);

            if (header.IsByron)
            {
                return FormatByronHeader(header);
            }

            BlockHeader blockHeader = CborSerializer.Deserialize<BlockHeader>(header.HeaderCbor);
            ulong slot = blockHeader.HeaderBody.Slot();
            ulong blockNumber = blockHeader.HeaderBody.BlockNumber();
            string hash = blockHeader.Hash();

            return $"[{header.Era}] rollforward slot {slot} block {blockNumber} hash {hash}";
        }
        catch (InvalidOperationException)
        {
            return $"rollforward tip {FormatPoint(rollForward.Tip.Slot)}";
        }
    }

    private static string FormatByronHeader(HeaderContent header)
    {
        string hash = Convert.ToHexStringLower(
            Blake2Fast.Blake2b.HashData(32, header.HeaderCbor.Span));

        if (header.IsByronEbb)
        {
            ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(header.HeaderCbor);
            return $"[{header.Era}] rollforward epoch {ebbHead.ConsensusData.EpochId} hash {hash}";
        }

        ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(header.HeaderCbor);
        ulong epoch = blockHead.ConsensusData.SlotId.Epoch;
        ulong relSlot = blockHead.ConsensusData.SlotId.Slot;
        ulong absSlot = (epoch * 21600) + relSlot;
        ulong blockNumber = blockHead.ConsensusData.Difficulty.GetValue().FirstOrDefault();

        return $"[{header.Era}] rollforward slot {absSlot} block {blockNumber} hash {hash}";
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

        string host = GetValue(map, "tcp-host") ?? GetEnv("TcpHost", "HOST") ?? DefaultHost;
        int port = ParseInt(GetValue(map, "tcp-port") ?? GetEnv("TcpPort", "PORT"), DefaultPort, "port", ref error);
        ulong magic = ParseUlong(GetValue(map, "magic") ?? GetEnv("NetworkMagic", "NETWORK_MAGIC"), DefaultNetworkMagic, "network magic", ref error);
        int keepAlive = ParseInt(
            GetValue(map, "keepalive") ?? GetEnv("KeepAliveIntervalSeconds", "KEEPALIVE_INTERVAL_SECONDS"),
            DefaultKeepAliveSeconds, "keepalive seconds", ref error);

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

        options = new Options(command, host, port, magic, keepAlive, slot, hash);
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
        Console.WriteLine("  chainsync    Sync headers from chain origin (default)");
        Console.WriteLine("  blockfetch   Fetch a single block by slot + hash");
        Console.WriteLine();
        Console.WriteLine("Common options (args or env):");
        Console.WriteLine("  --tcp-host <host>            TcpHost / HOST (default 127.0.0.1)");
        Console.WriteLine("  --tcp-port <port>            TcpPort / PORT (default 3001)");
        Console.WriteLine("  --magic <magic>              NetworkMagic / NETWORK_MAGIC (default 2)");
        Console.WriteLine("  --keepalive <seconds>        KeepAliveIntervalSeconds (default 20)");
        Console.WriteLine();
        Console.WriteLine("BlockFetch options:");
        Console.WriteLine("  --slot <slot>                Block slot number");
        Console.WriteLine("  --hash <hex>                 Block hash (hex-encoded)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- chainsync");
        Console.WriteLine("  dotnet run -- blockfetch --slot 12345 --hash abcdef...");
    }

    #endregion
}
