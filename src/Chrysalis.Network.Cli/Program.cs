using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
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
        string Host,
        int Port,
        ulong NetworkMagic,
        int KeepAliveSeconds
    );

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out Options options, out string? error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                Console.Error.WriteLine(error);
            PrintUsage();
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"Connecting to {options.Host}:{options.Port} (magic {options.NetworkMagic})...");

        using PeerClient peer = await PeerClient.ConnectAsync(options.Host, options.Port, cts.Token);
        await peer.StartAsync(options.NetworkMagic, TimeSpan.FromSeconds(options.KeepAliveSeconds));

        Console.WriteLine("Connected. Starting ChainSync...");

        ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([Point.Origin], cts.Token);

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

        while (!cts.Token.IsCancellationRequested)
        {
            MessageNextResponse? response = await peer.ChainSync.NextRequestAsync(cts.Token);

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
            }
        }

        if (peer.ChainSync.HasAgency)
        {
            try { await peer.ChainSync.DoneAsync(CancellationToken.None); }
            catch { /* Best-effort shutdown */ }
        }

        return 0;
    }

    private static string FormatRollForward(MessageRollForward rollForward)
    {
        try
        {
            HeaderContent header = HeaderContent.Decode(rollForward.Payload.Value);

            if (header.IsByron)
                return FormatByronHeader(header);

            BlockHeader blockHeader = CborSerializer.Deserialize<BlockHeader>(header.HeaderCbor);
            ulong slot = blockHeader.HeaderBody.Slot();
            ulong blockNumber = blockHeader.HeaderBody.BlockNumber();
            string hash = blockHeader.Hash();

            return $"[{header.Era}] rollforward slot {slot} block {blockNumber} hash {hash}";
        }
        catch
        {
            return $"rollforward tip {FormatPoint(rollForward.Tip.Slot)}";
        }
    }

    private static string FormatByronHeader(HeaderContent header)
    {
        string hash = Convert.ToHexString(
            Blake2Fast.Blake2b.HashData(32, header.HeaderCbor)).ToLowerInvariant();

        if (header.IsByronEbb)
        {
            ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(header.HeaderCbor);
            return $"[{header.Era}] rollforward epoch {ebbHead.ConsensusData.EpochId} hash {hash}";
        }

        ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(header.HeaderCbor);
        ulong epoch = blockHead.ConsensusData.SlotId.Epoch;
        ulong relSlot = blockHead.ConsensusData.SlotId.Slot;
        ulong absSlot = epoch * 21600 + relSlot;
        ulong blockNumber = blockHead.ConsensusData.Difficulty.GetValue().FirstOrDefault();

        return $"[{header.Era}] rollforward slot {absSlot} block {blockNumber} hash {hash}";
    }

    private static string FormatPoint(Point point) => point switch
    {
        SpecificPoint sp => $"slot {sp.Slot} hash {ToHex(sp.Hash)}",
        _ => "origin"
    };

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    #region Options parsing

    private static bool TryParseOptions(string[] args, out Options options, out string? error)
    {
        options = default!;
        error = null;

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
            return false;

        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
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

        if (!string.IsNullOrWhiteSpace(error))
            return false;

        options = new Options(host, port, magic, keepAlive);
        return true;
    }

    private static string? GetValue(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) ? value : null;

    private static string? GetEnv(params string[] names)
    {
        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static int ParseInt(string? value, int fallback, string label, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, out int parsed))
        {
            error = $"Invalid {label} value '{value}'.";
            return fallback;
        }
        return parsed;
    }

    private static ulong ParseUlong(string? value, ulong fallback, string label, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
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
        Console.WriteLine("  dotnet run --project src/Chrysalis.Network.Cli -- [options]");
        Console.WriteLine();
        Console.WriteLine("Syncs from chain origin (genesis). Options (args or env):");
        Console.WriteLine("  --tcp-host <host>            TcpHost / HOST (default 127.0.0.1)");
        Console.WriteLine("  --tcp-port <port>            TcpPort / PORT (default 3001)");
        Console.WriteLine("  --magic <magic>              NetworkMagic / NETWORK_MAGIC (default 2)");
        Console.WriteLine("  --keepalive <seconds>        KeepAliveIntervalSeconds / KEEPALIVE_INTERVAL_SECONDS (default 20)");
    }

    #endregion
}
