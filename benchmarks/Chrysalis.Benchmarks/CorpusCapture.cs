using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.MiniProtocols;
using System.Text;
using System.Text.Json;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Benchmarks;

internal static class CorpusCapture
{
    internal const ulong MainnetMagic = 764824073;

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length < 1 || args[0] is not ("tip" or "capture" or "headers")
            || args.Length != (args[0] switch { "tip" => 4, "capture" => 6, _ => 5 }))
        {
            throw new ArgumentException("corpus tip HOST PORT NEW_OUTPUT_DIRECTORY; capture adds ANCHOR_JSON DECLARATION_MD; headers adds RANGE_JSON.", nameof(args));
        }
        int port = int.Parse(args[2], CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        bool tipOnly = args[0] == "tip";
        bool capture = args[0] == "capture";
        RangePoint? anchor = null;
        FixedRange? range = null;
        int anchorNumber = 0;
        DateTimeOffset? anchorLookupUtc = null;
        byte[]? declarationBytes = null;
        CaptureDeclaration? declaration = null;
        if (capture)
        {
            using JsonDocument record = JsonDocument.Parse(await File.ReadAllTextAsync(args[4]).ConfigureAwait(false));
            anchor = record.RootElement.GetProperty("anchor").Deserialize<RangePoint>() ?? throw new InvalidDataException("Missing anchor.");
            anchorNumber = record.RootElement.GetProperty("blockNumber").GetInt32();
            anchorLookupUtc = record.RootElement.GetProperty("observedUtc").GetDateTimeOffset();
            declarationBytes = await File.ReadAllBytesAsync(args[5]).ConfigureAwait(false);
            declaration = ReadDeclaration(Encoding.UTF8.GetString(declarationBytes), anchorLookupUtc.Value);
            if (anchorLookupUtc > DateTimeOffset.UtcNow || anchor.Slot >= declaration.FirstSlot)
            {
                throw new InvalidDataException("The anchor lookup or anchor slot does not match the declaration.");
            }
        }
        else if (!tipOnly)
        {
            range = JsonSerializer.Deserialize<FixedRange>(await File.ReadAllTextAsync(args[4]).ConfigureAwait(false))
                ?? throw new InvalidDataException("Missing range.");
            if (range.Blocks.Length != 1000) { throw new InvalidDataException("The corpus requires 1,000 blocks."); }
            anchor = range.Anchor;
        }
        string root = args[3];
        if (Directory.Exists(root) || File.Exists(root)) { throw new IOException("The output directory must not exist."); }
        _ = Directory.CreateDirectory(root);
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        Stopwatch duration = Stopwatch.StartNew();
        RecordedBearer? bearer = null;
        CapturedBlock[] blocks = [];
        string? rangeSha256 = null;
        string status = "failed";
        string? error = null;
        object tools = ToolRecord();
        try
        {
            using CancellationTokenSource cancellation = new(tipOnly ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(30));
            if (declarationBytes is not null)
            {
                await File.WriteAllBytesAsync(Path.Combine(root, "declaration.md"), declarationBytes, cancellation.Token).ConfigureAwait(false);
            }
            bearer = await RecordedBearer.ConnectAsync(args[1], port, root, cancellation.Token).ConfigureAwait(false);
            using Plexer plexer = new(bearer);
            ConstructorInfo constructor = typeof(PeerClient).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, [typeof(Plexer)], null) ?? throw new InvalidOperationException("PeerClient setup constructor missing.");
            using PeerClient client = (PeerClient)constructor.Invoke([plexer]);
            await client.StartAsync(MainnetMagic, TimeSpan.FromHours(1))
                .WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
            ChainSyncMessage? intersection = await client.ChainSync.FindIntersectionAsync(
                [tipOnly ? Point.Origin : anchor!.ToPoint()], cancellation.Token).ConfigureAwait(false);
            if (intersection is not MessageIntersectFound found)
            {
                status = "intersection-not-found";
                await DoneAsync(client, cancellation.Token).ConfigureAwait(false);
                throw new InvalidDataException("The relay did not intersect at the requested point.");
            }
            RangePoint tip = TipPoint(found.Tip);
            int blockNumber = found.Tip.BlockNumber!.Value;
            if (tipOnly)
            {
                if (found.Point is not OriginPoint) { throw new InvalidDataException("The relay did not intersect at origin."); }
                await DoneAsync(client, cancellation.Token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(root, "tip.json"), JsonSerializer.Serialize(new
                {
                    relay = args[1],
                    port,
                    networkMagic = MainnetMagic,
                    startedUtc,
                    observedUtc = DateTimeOffset.UtcNow,
                    anchor = tip,
                    blockNumber,
                    runtime = Environment.Version.ToString()
                }, Program.IndentedJson), cancellation.Token).ConfigureAwait(false);
                Console.WriteLine($"Tip recorded: slot {tip.Slot}, block {blockNumber}, hash {tip.Hash}.");
            }
            else
            {
                RequirePoint(found.Point, anchor!);
                if (capture && blockNumber < (long)anchorNumber + 3160)
                {
                    status = "insufficient-depth";
                    await DoneAsync(client, cancellation.Token).ConfigureAwait(false);
                    throw new InvalidDataException("The relay tip is fewer than 3,160 blocks above the anchor.");
                }
                Heartbeat heartbeat = new(client.KeepAlive, TimeSpan.FromSeconds(20));
                RangePoint[] points = await ReadHeadersAsync(client.ChainSync, heartbeat, anchor!, range?.Blocks, declaration?.FirstSlot, cancellation.Token).ConfigureAwait(false);
                if (capture)
                {
                    range = new(anchor!, points);
                    await File.WriteAllTextAsync(Path.Combine(root, "range.json"), JsonSerializer.Serialize(range, Program.IndentedJson), cancellation.Token).ConfigureAwait(false);
                    rangeSha256 = HashFile(Path.Combine(root, "range.json"));
                    blocks = await FetchAsync(client.BlockFetch, heartbeat, range, cancellation.Token).ConfigureAwait(false);
                    range = new(anchor!, [.. points.Select((point, index) => point with { Sha256 = blocks[index].Sha256 })]);
                    await File.WriteAllTextAsync(Path.Combine(root, "range.json"), JsonSerializer.Serialize(range, Program.IndentedJson), cancellation.Token).ConfigureAwait(false);
                    rangeSha256 = HashFile(Path.Combine(root, "range.json"));
                }
                else { rangeSha256 = HashFile(args[4]); }
                await DoneAsync(client, cancellation.Token).ConfigureAwait(false);
                Console.WriteLine($"Recorded {points.Length} headers and {blocks.Length} blocks.");
            }
            status = "ok";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Network exception text may contain local endpoints. Keep only its type.
            error = exception.GetType().Name;
            throw new InvalidOperationException("Corpus attempt failed (" + error + "); partial recording retained.");
        }
        finally
        {
            bearer?.Dispose();
            string json = JsonSerializer.Serialize(new
            {
                step = args[0],
                relay = args[1],
                port,
                networkMagic = MainnetMagic,
                anchorLookupUtc,
                startedUtc,
                completedUtc = DateTimeOffset.UtcNow,
                seconds = duration.Elapsed.TotalSeconds,
                status,
                error,
                tools,
                blocks = blocks.Select(block => new { bytes = block.Bytes, sha256 = block.Sha256 }),
                rawBytes = blocks.Sum(block => (long)block.Bytes),
                rangeSha256,
                events = bearer?.Events
            }, Program.IndentedJson);
            await File.WriteAllTextAsync(Path.Combine(root, tipOnly ? "attempt.json" : "capture.json"), json).ConfigureAwait(false);
        }
    }

    private static async Task DoneAsync(PeerClient client, CancellationToken token)
    {
        await client.ChainSync.DoneAsync(token).ConfigureAwait(false);
        await client.BlockFetch.DoneAsync(token).ConfigureAwait(false);
        await client.KeepAlive.SendDoneAsync(token).ConfigureAwait(false);
    }

    private static void RequirePoint(Point actual, RangePoint expected)
    {
        if (actual is not SpecificPoint point || point.Slot != expected.Slot
            || !point.Hash.Span.SequenceEqual(Convert.FromHexString(expected.Hash)))
        {
            throw new InvalidDataException("The relay point differs from the anchor.");
        }
    }

    private static async Task<RangePoint[]> ReadHeadersAsync(ChainSync chain, Heartbeat heartbeat, RangePoint anchor,
        RangePoint[]? expected, ulong? minimumSlot, CancellationToken token)
    {
        if (await chain.NextRequestAsync(token).ConfigureAwait(false) is not MessageRollBackward rollback)
        {
            throw new InvalidDataException("The range must start with one rollback.");
        }
        RequirePoint(rollback.Point, anchor);
        RangePoint[] points = new RangePoint[1000];
        ulong bodyBytes = 0;
        int overFrame = 0;
        int overUshort = 0;
        for (int index = 0; index < points.Length; index++)
        {
            await heartbeat.PulseAsync(token).ConfigureAwait(false);
            if (await chain.NextRequestAsync(token).ConfigureAwait(false) is not N2NMessageRollForward forward)
            {
                throw new InvalidDataException("The range must have 1,000 consecutive RollForward messages.");
            }
            ChainSyncHeader header = ChainSyncHeader.Decode(forward.Payload.Raw);
            if (header.Variant != 6) { throw new InvalidDataException("The corpus must contain Conway headers."); }
            ChainPoint point = header.ExtractPoint();
            if (index == 0 && minimumSlot.HasValue && point.Slot < minimumSlot.Value)
            {
                throw new InvalidDataException("The first block precedes the declaration boundary.");
            }
            points[index] = new(point.Slot, Convert.ToHexStringLower(point.Hash), expected?[index].Sha256 ?? "");
            if (expected is not null && (points[index].Slot != expected[index].Slot || points[index].Hash != expected[index].Hash))
            {
                throw new InvalidDataException("The second relay served a different range.");
            }
            ulong size = CborSerializer.Deserialize<BlockHeader>(header.HeaderCbor).HeaderBody.BlockBodySize();
            bodyBytes += size;
            if (size > 12288) { overFrame++; }
            if (size > 65535) { overUshort++; }
        }
        await heartbeat.CompleteAsync().ConfigureAwait(false);
        Console.WriteLine($"Headers: {bodyBytes} body bytes, {overFrame} over 12,288 B, {overUshort} over 65,535 B.");
        return points;
    }

    internal sealed record CaptureDeclaration(DateTimeOffset DeclaredUtc, DateTimeOffset T0Utc, ulong FirstSlot);

    internal static CaptureDeclaration ReadDeclaration(string text, DateTimeOffset firstFetchUtc)
    {
        string[] lines = text.Split('\n');
        string Value(string prefix) => lines.SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim()
            ?? throw new InvalidDataException("Missing declaration field: " + prefix);
        DateTimeOffset declared = DateTimeOffset.Parse(Value("- Declaration time: "), CultureInfo.InvariantCulture);
        DateTimeOffset t0 = DateTimeOffset.Parse(Value("- T0 = ").Split(' ')[0], CultureInfo.InvariantCulture);
        ulong slot = ulong.Parse(Value("- Mainnet slot at T0: ").Split('.')[0], CultureInfo.InvariantCulture);
        DateTimeOffset past = declared.AddHours(-48);
        DateTimeOffset expected = new(past.Year, past.Month, past.Day, past.Hour, 0, 0, TimeSpan.Zero);
        if (declared.Offset != TimeSpan.Zero || t0.Offset != TimeSpan.Zero || t0 != expected || declared >= firstFetchUtc)
        {
            throw new InvalidDataException("The declaration must fix the 48-hour, hour-rounded T0 before the first fetch.");
        }
        return new(declared, t0, slot);
    }

    private static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static object ToolRecord()
    {
        ProcessStartInfo info = new("git") { WorkingDirectory = Fixtures.Root, RedirectStandardOutput = true };
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.ArgumentList.Add("rev-parse"); info.ArgumentList.Add("HEAD");
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot read the capture commit.");
        string commit = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) { throw new InvalidOperationException("Cannot read the capture commit."); }
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in Directory.GetFiles(Fixtures.Root, "*.cs").Order(StringComparer.Ordinal))
        {
            sourceHash.AppendData(File.ReadAllBytes(path));
        }
        return new
        {
            repositoryCommit = commit,
            harnessSha256 = Convert.ToHexStringLower(sourceHash.GetHashAndReset()),
            harnessAssemblySha256 = HashFile(typeof(CorpusCapture).Assembly.Location),
            clientAssemblySha256 = HashFile(typeof(PeerClient).Assembly.Location),
            baseline = "7c45d47c87c63520cea84212dbd01ecc8d372ac3",
            sdk = "10.0.301",
            runtime = Environment.Version.ToString()
        };
    }

    internal static async Task<CapturedBlock[]> FetchAsync(BlockFetch fetch, Heartbeat heartbeat, FixedRange range, CancellationToken token)
    {
        List<CapturedBlock> blocks = [];
        await foreach (BlockWithEra block in fetch.FetchRangeAsync<BlockWithEra>(range.Blocks[0].ToPoint(), range.Blocks[^1].ToPoint(), token).ConfigureAwait(false))
        {
            if (blocks.Count >= range.Blocks.Length) { throw new InvalidDataException("The relay sent extra blocks."); }
            RangePoint expected = range.Blocks[blocks.Count];
            string digest = Convert.ToHexStringLower(SHA256.HashData(block.Raw.Span));
            if (block.Block.Slot() != expected.Slot || block.Block.Header().Hash() != expected.Hash
                || (expected.Sha256.Length != 0 && expected.Sha256 != digest))
            {
                throw new InvalidDataException("A fetched block differs from its recorded header or digest.");
            }
            blocks.Add(new(block.Raw.Length, digest));
            await heartbeat.PulseAsync(token).ConfigureAwait(false);
        }
        if (blocks.Count != range.Blocks.Length) { throw new InvalidDataException("The relay returned an incomplete block range."); }
        await heartbeat.CompleteAsync().ConfigureAwait(false);
        return [.. blocks];
    }

    internal sealed record CapturedBlock(int Bytes, string Sha256);

    internal sealed class Heartbeat(KeepAliveClient keepAlive, TimeSpan interval)
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private Task? pending;

        internal async Task PulseAsync(CancellationToken token)
        {
            if (pending is not null)
            {
                if (!pending.IsCompleted) { return; }
                await pending.ConfigureAwait(false);
                pending = null;
            }
            if (elapsed.Elapsed < interval) { return; }
            await keepAlive.SendKeepAliveRequestAsync(token).ConfigureAwait(false);
            pending = keepAlive.ReceiveKeepAliveResponseAsync(token);
            elapsed.Restart();
        }

        internal async Task CompleteAsync()
        {
            if (pending is not null) { await pending.ConfigureAwait(false); pending = null; }
        }
    }

    internal static RangePoint TipPoint(Tip tip)
    {
        if (tip.Slot is not SpecificPoint point || point.Hash.Length != 32 || tip.BlockNumber is not > 0)
        {
            throw new InvalidDataException("The relay tip must have a point and a positive block number.");
        }
        return new(point.Slot, Convert.ToHexStringLower(point.Hash.Span), "");
    }
}

internal sealed class RecordedBearer : IBearer
{
    private readonly TcpClient client;
    private readonly RecordingStream stream;
    private bool disposed;
    internal IReadOnlyList<object> Events => stream.Events;
    public PipeReader Reader { get; }
    public PipeWriter Writer { get; }

    private RecordedBearer(TcpClient client, RecordingStream stream)
    {
        this.client = client; this.stream = stream;
        Reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 65_536, minimumReadSize: 32_768, leaveOpen: true));
        Writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(minimumBufferSize: 4_096, leaveOpen: true));
    }

    internal static async Task<RecordedBearer> ConnectAsync(string host, int port, string root, CancellationToken token)
    {
        TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, port, token).ConfigureAwait(false);
            client.NoDelay = true; client.SendBufferSize = 131_072; client.ReceiveBufferSize = 262_144;
            return new(client, new RecordingStream(client.GetStream(), root));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Reader.CancelPendingRead(); Writer.CancelPendingFlush();
        stream.Dispose();
        Reader.Complete(); Writer.Complete(); client.Dispose();
    }
}

internal sealed class RecordingStream(NetworkStream inner, string root) : Stream
{
    private readonly FileStream received = File.Create(Path.Combine(root, "server-to-client.bin"));
    private readonly FileStream sent = File.Create(Path.Combine(root, "client-to-server.bin"));
    private readonly Lock gate = new();
    private readonly List<object> events = [];
    internal IReadOnlyList<object> Events { get { lock (gate) { return [.. events]; } } }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    private void Record(bool inbound, ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            FileStream target = inbound ? received : sent;
            long offset = target.Position;
            target.Write(bytes);
            events.Add(new { inbound, offset, length = bytes.Length, timestamp = Stopwatch.GetTimestamp() });
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        if (read != 0) { Record(true, buffer.AsSpan(offset, read)); }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read != 0) { Record(true, buffer.Span[..read]); }
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count); Record(false, buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); Record(false, buffer.Span);
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { inner.Dispose(); received.Dispose(); sent.Dispose(); }
        base.Dispose(disposing);
    }
}
