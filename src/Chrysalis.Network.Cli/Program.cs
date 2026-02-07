using System.Formats.Cbor;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
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
        int KeepAliveSeconds,
        ulong StartSlot,
        byte[] StartHash
    );

    private sealed record HeaderInfo(ulong Slot, ulong BlockNumber, string Hash);

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out Options options, out string? error))
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
            }
            PrintUsage();
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine(
            $"Connecting to {options.Host}:{options.Port} (magic {options.NetworkMagic})...");

        using PeerClient peer = await PeerClient.ConnectAsync(options.Host, options.Port, cts.Token);
        await peer.StartAsync(options.NetworkMagic, TimeSpan.FromSeconds(options.KeepAliveSeconds));

        Console.WriteLine("Connected. Starting ChainSync...");

        Point startPoint = new(options.StartSlot, options.StartHash);
        ChainSyncMessage intersect = await peer.ChainSync.FindIntersectionAsync([startPoint], cts.Token);

        switch (intersect)
        {
            case MessageIntersectFound found:
                Console.WriteLine(
                    $"Intersection found at slot {found.Point.Slot} hash {ToHex(found.Point.Hash)}");
                break;
            case MessageIntersectNotFound notFound:
                Console.WriteLine(
                    $"Intersection not found. Tip slot {notFound.Tip.Slot.Slot} hash {ToHex(notFound.Tip.Slot.Hash)}");
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
                    if (TryDecodeHeader(rollForward.Payload.Value, out HeaderInfo header))
                    {
                        Console.WriteLine(
                            $"rollforward slot {header.Slot} hash {header.Hash} block {header.BlockNumber}");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"rollforward tip slot {rollForward.Tip.Slot.Slot} hash {ToHex(rollForward.Tip.Slot.Hash)}");
                    }
                    break;

                case MessageRollBackward rollBackward:
                    atTip = false;
                    Console.WriteLine(
                        $"rollback to slot {rollBackward.Point.Slot} hash {ToHex(rollBackward.Point.Hash)}");
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
            try
            {
                await peer.ChainSync.DoneAsync(CancellationToken.None);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        return 0;
    }

    private static bool TryParseOptions(string[] args, out Options options, out string? error)
    {
        options = default!;
        error = null;

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

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
            DefaultKeepAliveSeconds,
            "keepalive seconds",
            ref error);

        if (!string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        string? slotRaw = GetValue(map, "slot") ?? GetEnv("Slot", "SLOT");
        string? hashRaw = GetValue(map, "hash") ?? GetEnv("Hash", "HASH");

        if (string.IsNullOrWhiteSpace(slotRaw) || string.IsNullOrWhiteSpace(hashRaw))
        {
            error = "Both slot and hash are required.";
            return false;
        }

        if (!ulong.TryParse(slotRaw, out ulong slot))
        {
            error = $"Invalid slot value '{slotRaw}'.";
            return false;
        }

        if (!TryParseHash(hashRaw, out byte[] hash, out error))
        {
            return false;
        }

        options = new Options(host, port, magic, keepAlive, slot, hash);
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

    private static bool TryParseHash(string value, out byte[] hash, out string? error)
    {
        hash = Array.Empty<byte>();
        error = null;

        string normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0 || normalized.Length % 2 != 0)
        {
            error = "Hash must be a non-empty hex string with an even length.";
            return false;
        }

        try
        {
            hash = Convert.FromHexString(normalized);
            return true;
        }
        catch (FormatException)
        {
            error = "Hash must be a valid hex string.";
            return false;
        }
    }

    private static bool TryDecodeHeader(ReadOnlyMemory<byte> payload, out HeaderInfo headerInfo)
    {
        headerInfo = default!;

        if (payload.IsEmpty)
        {
            return false;
        }

        if (!TryExtractHeaderBytes(payload, out byte variant, out byte[] headerBytes))
        {
            return false;
        }

        if (variant == 0)
        {
            return false;
        }

        try
        {
            BlockHeader header = CborSerializer.Deserialize<BlockHeader>(headerBytes);
            ulong slot = header.HeaderBody.Slot();
            ulong blockNumber = header.HeaderBody.BlockNumber();
            string hash = header.Hash();
            headerInfo = new HeaderInfo(slot, blockNumber, hash);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractHeaderBytes(ReadOnlyMemory<byte> payload, out byte variant, out byte[] headerBytes)
    {
        variant = 0;
        headerBytes = Array.Empty<byte>();

        try
        {
            CborReader reader = new(payload, CborConformanceMode.Lax);
            int? outerLength = reader.ReadStartArray();

            variant = checked((byte)reader.ReadUInt64());

            if (variant == 0)
            {
                int? innerLength = reader.ReadStartArray();
                int? prefixLength = reader.ReadStartArray();
                _ = reader.ReadUInt64();
                _ = reader.ReadUInt64();

                if (prefixLength is null)
                {
                    reader.ReadEndArray();
                }

                _ = reader.ReadTag();
                headerBytes = reader.ReadByteString();

                if (innerLength is null)
                {
                    reader.ReadEndArray();
                }
            }
            else
            {
                _ = reader.ReadTag();
                headerBytes = reader.ReadByteString();
            }

            if (outerLength is null)
            {
                reader.ReadEndArray();
            }

            return headerBytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/Chrysalis.Network.Cli -- --slot <slot> --hash <hex> [options]");
        Console.WriteLine("");
        Console.WriteLine("Options (args or env):");
        Console.WriteLine("  --tcp-host <host>            TcpHost / HOST (default 127.0.0.1)");
        Console.WriteLine("  --tcp-port <port>            TcpPort / PORT (default 3001)");
        Console.WriteLine("  --magic <magic>              NetworkMagic / NETWORK_MAGIC (default 2)");
        Console.WriteLine("  --keepalive <seconds>        KeepAliveIntervalSeconds / KEEPALIVE_INTERVAL_SECONDS (default 20)");
        Console.WriteLine("  --slot <slot>                Slot / SLOT (required)");
        Console.WriteLine("  --hash <hex>                 Hash / HASH (required)");
    }
}
