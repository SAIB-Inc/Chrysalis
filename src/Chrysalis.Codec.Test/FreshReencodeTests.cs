using System.Buffers;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Network.Cbor.BlockFetch;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

public class FreshReencodeTests
{
    private delegate ICborType Decode(ReadOnlyMemory<byte> bytes, out int consumed);
    private const string LocalManifest = "x8-local.tsv";
    private const string PallasManifest = "pallas-x8.tsv";
    private const string MainnetDeferredManifest = "x8-mainnet-deferred.tsv";
    private static readonly string[] PreprodFixtureFolders = ["blocks", "transactions"];
    private const string LocalInventory = "adcfbed2579482de0e4851ba1b9e534d99e623a7b640a197fdaf8f016077c0c0";
    private const string PallasInventory = "d30f7cf1d9ddceca33f4731396d34e9f6108880faf301d9c76697c6b057b6250";
    private static readonly JsonSerializerOptions ProjectionOptions = new() { MaxDepth = 256 };

    private static readonly Lazy<Dictionary<string, string[]>> Differences = new(() =>
        Manifest("x8-differences.tsv", 4, 0).ToDictionary(row => row[0], StringComparer.Ordinal));

    private static readonly Lazy<HashSet<string>> Owners = new(() => ManifestLines(LocalManifest)
        .Where(line => line.StartsWith("# owner\t", StringComparison.Ordinal))
        .Select(line => line[8..]).ToHashSet(StringComparer.Ordinal));

    public static IEnumerable<object[]> LocalRows() => ReplayRows(LocalManifest, LocalInventory, 58, 42);

    public static IEnumerable<object[]> PallasRows() => ReplayRows(PallasManifest, PallasInventory, 94, 61);

    private static string[][] ReplayRows(string name, string inventory, int total, int active)
    {
        string[][] rows = Manifest(name, 4, 1);
        ValidateInventory(rows, name, inventory);
        Assert.Equal(total, rows.Length);
        string[][] replay = [.. rows.Where(row => row[3] == "replay").Select(row => row[..3])];
        Assert.Equal(active, replay.Length);
        return replay;
    }

    private static void ValidateInventory(string[][] rows, string name, string inventory)
    {
        foreach (string[] row in rows)
        {
            Assert.True(row[3] == "replay" || Owners.Value.Contains(row[3]) || (name == PallasManifest && row[3] == "local-mirror"),
                $"Unknown disposition '{row[3]}' in {name}: {row[1]}.");
        }
        // Pin file, decoder and source hash, independently of row order and ownership transfers.
        string identities = string.Join('\n', rows.OrderBy(row => row[1], StringComparer.Ordinal)
            .Select(row => string.Join('\t', row[..3])));
        Assert.True(inventory == Sha(System.Text.Encoding.UTF8.GetBytes(identities)), $"Source inventory changed: {name}.");
    }

    private static Dictionary<string, string[]> MainnetDeferred()
    {
        string[][] rows = Manifest(MainnetDeferredManifest, 3, 0);
        _ = Assert.Single(rows);
        foreach (string[] row in rows)
        {
            Assert.True(Owners.Value.Contains(row[2]), $"Unknown mainnet deferral owner '{row[2]}': {row[0]}.");
        }
        return rows.ToDictionary(row => row[0], StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LocalRows))]
    public void LocalFixture(string digest, string file, string codec)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[] bytes = Verified(LocalPath(file), digest);
        if (codec == "cli-conway")
        {
            bytes = X10CborListTests.ReadCliWire(bytes);
            codec = nameof(Era.Conway);
        }
        else if (!file.EndsWith(".cbor", StringComparison.Ordinal))
        {
            bytes = Hex(bytes);
        }
        Reencode($"local/{file}", bytes, codec);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [Trait("Corpus", "CodecReencode")]
    [MemberData(nameof(PallasRows))]
    public void PallasFixture(string digest, string file, string codec)
    {
        string? root = Environment.GetEnvironmentVariable("CHRYSALIS_X8_PALLAS");
        Assert.False(string.IsNullOrWhiteSpace(root), "Run TestData/FetchX8Pallas.py and set CHRYSALIS_X8_PALLAS.");
        Reencode($"pallas/{file}", Hex(Verified(Path.Combine(root!, file), digest)), codec);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Corpus", "CodecReencode")]
    public void MainnetBlockCorpus()
    {
        string? root = Environment.GetEnvironmentVariable("CHRYSALIS_X8_MAINNET");
        Assert.False(string.IsNullOrWhiteSpace(root), "Set CHRYSALIS_X8_MAINNET to the verified public capture.");
        byte[] index = Verified(Path.Combine(root!, "corpus.json"), "2794807d0226dc2a8cfa69f71ddb55eecc266b893a0e6de57c9177b80e017935");
        byte[] frames = Verified(Path.Combine(root!, "server-to-client.bin"), "01007e6529e417bd252f91031fdf6f81afd8df300e744a8694754c11cf307e44");
        using JsonDocument corpus = JsonDocument.Parse(index);
        Assert.Equal("recorded-frames-v1", corpus.RootElement.GetProperty("format").GetString());
        ArrayBufferWriter<byte> payload = new();
        foreach (JsonElement group in corpus.RootElement.GetProperty("bodies").EnumerateArray())
        {
            foreach (JsonElement frame in group.EnumerateArray())
            {
                const int muxHeaderLength = 8;
                payload.Write(frames.AsSpan(frame.GetProperty("offset").GetInt32() + muxHeaderLength,
                    frame.GetProperty("length").GetInt32() - muxHeaderLength));
            }
        }

        ReadOnlyMemory<byte> messages = payload.WrittenMemory;
        _ = Assert.IsType<StartBatch>(BlockFetchMessage.Read(messages, out int consumed));
        messages = messages[consumed..];
        Dictionary<string, string[]> deferred = MainnetDeferred();
        List<string> errors = [];
        for (int i = 0; i < 1000; i++)
        {
            BlockBody block = Assert.IsType<BlockBody>(BlockFetchMessage.Read(messages, out consumed));
            messages = messages[consumed..];
            string fixture = $"mainnet/{i:D4}";
            if (deferred.Remove(fixture, out string[]? disposition))
            {
                Assert.Equal(disposition[1], Sha(block.Body.Value.Span));
                Assert.False(Differences.Value.ContainsKey(fixture), $"Deferred fixture has a difference pin: {fixture}");
                continue;
            }
            try
            {
                _ = Reencode(fixture, block.Body.Value, Read<BlockWithEra>);
            }
            catch (Exception error)
            {
                errors.Add($"{fixture}: {error.Message}");
            }
        }
        _ = Assert.IsType<BatchDone>(BlockFetchMessage.Read(messages, out consumed));
        Assert.Equal(messages.Length, consumed);
        Assert.Empty(deferred);
        Assert.True(errors.Count == 0, string.Join('\n', errors));
    }

    [Fact]
    public void DifferencePinsNameReplayRows()
    {
        HashSet<string> local = LocalRows().Select(row => $"local/{row[1]}").ToHashSet(StringComparer.Ordinal);
        HashSet<string> pallas = PallasRows().Select(row => $"pallas/{row[1]}").ToHashSet(StringComparer.Ordinal);
        HashSet<string> deferred = MainnetDeferred().Keys.ToHashSet(StringComparer.Ordinal);
        Assert.All(Differences.Value.Keys, fixture => Assert.True(local.Contains(fixture) || pallas.Contains(fixture) ||
            (fixture.StartsWith("mainnet/", StringComparison.Ordinal) && fixture.Length == 12 &&
             int.TryParse(fixture.AsSpan(8), out int slot) && slot is >= 0 and < 1000 && !deferred.Contains(fixture)),
            $"Unknown or deferred replay pin: {fixture}"));
    }

    [Fact]
    public void LocalFilesAndPallasMirrorsMatchInventory()
    {
        string[][] local = Manifest(LocalManifest, 4, 1);
        string[][] pallas = Manifest(PallasManifest, 4, 1);
        HashSet<string> names = local.Select(row => row[1]).ToHashSet(StringComparer.Ordinal);
        HashSet<string> hashes = local.Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(15, pallas.Count(row => row[3] == "local-mirror"));
        Assert.All(pallas, row => Assert.Equal(hashes.Contains(row[0]), row[3] == "local-mirror"));

        string data = Path.GetDirectoryName(LocalPath(LocalManifest))!;
        IEnumerable<string> inTree = Directory.EnumerateFiles(data)
            .Where(path => Path.GetExtension(path) is ".block" or ".header" or ".tx").Select(path => Path.GetFileName(path));
        IEnumerable<string> vendored = Directory.EnumerateFiles(Path.Combine(data, "X8Sources"), "*.cbor", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(data, path).Replace(Path.DirectorySeparatorChar, '/'));
        IEnumerable<string> preprod = PreprodFixtureFolders.SelectMany(folder =>
            Directory.EnumerateFiles(LocalPath($"g1/{folder}"), "*.cbor").Select(path => $"g1/{folder}/{Path.GetFileName(path)}"));
        Assert.All(inTree.Concat(vendored).Concat(preprod), file => Assert.Contains(file, names));
        foreach (string[] row in local.Where(row => row[3] != "replay" && !UnvendoredByron(row[1])))
        {
            _ = Verified(LocalPath(row[1]), row[0]);
        }
    }

    private static bool UnvendoredByron(string file) => file is
        "X8Sources/consensus/disk-Byron_EBB.cbor" or "X8Sources/consensus/disk-Byron_regular.cbor" or
        "X8Sources/consensus/n2n-Byron_EBB.cbor" or "X8Sources/consensus/n2n-Byron_regular.cbor";

    [Theory]
    [InlineData(true, "Unknown disposition")]
    [InlineData(false, "Source inventory changed")]
    public void InventoryCannotSilentlyDropStrictRows(bool misspell, string error)
    {
        string[][] rows = Manifest(LocalManifest, 4, 1);
        string[] strict = Assert.Single(rows, row => row[1] == "allegra1.block");
        Assert.Equal("replay", strict[3]);
        if (misspell)
        {
            strict[3] = "repl ay";
        }
        else
        {
            rows = [.. rows.Where(row => row != strict)];
        }
        Exception failure = Assert.Throws<Xunit.Sdk.TrueException>(() => ValidateInventory(rows, LocalManifest, LocalInventory));
        Assert.Contains(error, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A200010203", "A202030001", "map-order")]
    [InlineData("BF00010203FF", "A200010203", "map-framing")]
    [InlineData("9F0102FF", "820102", "array-framing")]
    [InlineData("A200010002", "A200020001", null)] // duplicate key values exchanged
    [InlineData("A200010203", "A202010003", null)] // values assigned to the wrong keys
    [InlineData("A10001", "A10002", null)] // leaf changed
    [InlineData("D901028101", "8101", null)] // tag lost
    [InlineData("A2810001810102", "A2810102810001", null)] // composite keys do not move
    [InlineData("820102", "98020102", null)] // definite array header widened
    [InlineData("9F0102FF", "98020102", null)] // definite side must use preferred width
    [InlineData("5F5801AAFF", "5F41AAFF", "argument-width")]
    [InlineData("5F41AAFF", "5F5801AAFF", null)] // fresh chunk widened
    [InlineData("5F5801AAFF", "5F41BBFF", null)] // chunk payload changed
    [InlineData("5801AA", "41AA", null)] // not a chunk
    public void ClassifierProvesOnlyKnownWireDifferences(string original, string fresh, string? expected)
    {
        string classes = X8WireDifferences.Classify(Convert.FromHexString(original), Convert.FromHexString(fresh));
        if (expected is null)
        {
            Assert.StartsWith("unexplained", classes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(expected, classes);
        }
    }

    // Conway CDDL at 95b369053fd32422695a81fb7d5289d6dd35eee2:
    // multiasset<a> permits zero outer pairs; transaction_id is hash32;
    // vkeywitnesses use a nonempty list (optionally tagged), not a deduplicating collection.
    [Fact]
    public void ConwayAcceptsEmptyOuterMultiAsset()
    {
        byte[] bytes = [0x82, 0x01, 0xA0];
        LovelaceWithMultiAsset amount = LovelaceWithMultiAsset.Read(bytes);
        Assert.Equal(1UL, amount.Amount);
        Assert.Empty(amount.MultiAsset.Value);
        _ = Reencode("Conway empty multiasset", bytes, Read<LovelaceWithMultiAsset>);
    }

    [Fact]
    public void ConwayAccepts32ByteTransactionId()
    {
        byte[] bytes = [0x82, 0x58, 32, .. new byte[32], 0x00];
        Assert.Equal(32, CborSerializer.Deserialize<TransactionInput>(bytes).TransactionId.Length);
        _ = Reencode("Conway 32-byte transaction id", bytes, Read<TransactionInput>);
    }

    [Fact]
    public void ConwayPreservesRepeatedWitnesses()
    {
        byte[] witness = [0x82, 0x58, 0x20, .. new byte[32], 0x58, 0x40, .. new byte[64]];
        byte[] bytes = [0xA1, 0x00, 0xD9, 0x01, 0x02, 0x82, .. witness, .. witness];
        PostAlonzoTransactionWitnessSet witnesses = PostAlonzoTransactionWitnessSet.Read(bytes);
        Assert.Collection(witnesses.VKeyWitnesses!.Value,
            entry => Assert.Equal(witness, entry.Raw.ToArray()),
            entry => Assert.Equal(witness, entry.Raw.ToArray()));
        _ = Reencode("Conway repeated witnesses", bytes, Read<PostAlonzoTransactionWitnessSet>);
    }

    [Fact]
    public void RelayKeepsIpv4OctetsInWireOrder()
    {
        // Pallas daecef59:test_data/alonzo2.block, single_host_addr in a pool registration.
        byte[] wire = Convert.FromHexString("84001917704447F4A4CDF6");
        SingleHostAddr relay = CborSerializer.Deserialize<SingleHostAddr>(wire, out int consumed);
        Assert.Equal(wire.Length, consumed);
        byte[] address = [0x47, 0xF4, 0xA4, 0xCD];
        Assert.Equal(address, relay.Ipv4!.Value.ToArray());
    }

    private static string[] ManifestLines(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static string[][] Manifest(string name, int columns, int key)
    {
        string[][] rows = [.. ManifestLines(name).Where(line => !line.StartsWith('#', StringComparison.Ordinal)).Select(line => line.Split('\t'))];
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(columns, row.Length));
        Assert.Equal(rows.Length, rows.Select(row => row[key]).Distinct(StringComparer.Ordinal).Count());
        return rows;
    }

    private static string LocalPath(string file)
    {
        string root = RepositoryRoot();
        return file.StartsWith("g1/", StringComparison.Ordinal)
            ? Path.Combine(root, "src", "Chrysalis.Network.Tests", "Fixtures", "Preprod", file[3..])
            : Path.Combine(root, "src", "Chrysalis.Codec.Test", "TestData", file);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? path = new(AppContext.BaseDirectory); path is not null; path = path.Parent)
        {
            if (File.Exists(Path.Combine(path.FullName, "Chrysalis.slnx")))
            {
                return path.FullName;
            }
        }
        throw new DirectoryNotFoundException("Cannot locate Chrysalis fixture sources from the test assembly.");
    }

    private static byte[] Verified(string path, string digest)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(digest == Sha(bytes), $"SHA-256 mismatch: {Path.GetFileName(path)}");
        return bytes;
    }

    private static byte[] Hex(byte[] bytes) => Convert.FromHexString(System.Text.Encoding.UTF8.GetString(bytes).Trim());

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Reencode(string fixture, ReadOnlyMemory<byte> bytes, string codec)
    {
        if (codec == "block-n2n")
        {
            CborReader reader = new(bytes.Span);
            Assert.True(reader.TryReadSemanticTag(out ulong tag));
            Assert.Equal(24UL, tag);
            byte[] body = reader.ReadByteStringToArray();
            Assert.True(reader.Buffer.IsEmpty);
            // Pins explain just the independently rebuilt payload. Require the original outer framing
            // to be exactly what the tag-24 envelope writer emits, for the original payload length.
            Assert.True(bytes.Span.SequenceEqual(TaggedBlock(body).WrittenSpan), $"Tag-24 block framing differs: {fixture}");
            ReadOnlyMemory<byte> fresh = Reencode(fixture, body, Read<BlockWithEra>);
            CborReader envelope = new(TaggedBlock(fresh).WrittenSpan);
            Assert.True(envelope.TryReadSemanticTag(out tag));
            Assert.Equal(24UL, tag);
            Assert.True(fresh.Span.SequenceEqual(envelope.ReadByteStringToArray()));
            Assert.True(envelope.Buffer.IsEmpty);
            return;
        }
        Decode read = codec switch
        {
            "block" => Read<BlockWithEra>,
            "block-alonzo" => Read<AlonzoCompatibleBlock>,
            "block-babbage" => Read<BabbageBlock>,
            "block-conway" => Read<ConwayBlock>,
            "pallas-legacy-tx" => (raw, out consumed) => ITransaction.Read(raw, Era.Alonzo, out consumed),
            "Byron" => Read<ByronTxPayload>,
            "byron-header" => Read<ByronBlockHead>,
            "header" => Read<BlockHeader>,
            _ when Enum.TryParse(codec, out Era era) => (raw, out consumed) => ITransaction.Read(raw, era, out consumed),
            _ => throw new InvalidOperationException($"Unknown fixture decoder: {codec}")
        };
        _ = Reencode(fixture, bytes, read);
    }

    private static ArrayBufferWriter<byte> TaggedBlock(ReadOnlyMemory<byte> body)
    {
        ArrayBufferWriter<byte> output = new();
        CborWriter writer = new(output);
        writer.WriteSemanticTag(24);
        writer.WriteByteString(body.Span);
        return output;
    }

    private static ICborType Read<T>(ReadOnlyMemory<byte> bytes, out int consumed) where T : ICborType =>
        CborSerializer.Deserialize<T>(bytes, out consumed);

    private static ReadOnlyMemory<byte> Reencode(string fixture, ReadOnlyMemory<byte> bytes, Decode read)
    {
        ICborType decoded = read(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        Assert.True(bytes.Span.SequenceEqual(decoded.Raw.Span), $"Preserved bytes differ: {fixture}");
        ICborType rebuilt = Assert.IsAssignableFrom<ICborType>(Rebuild(decoded));
        Assert.False(rebuilt.Raw.IsEmpty);
        ICborType reread = read(rebuilt.Raw, out consumed);
        Assert.Equal(rebuilt.Raw.Length, consumed);
        Assert.Equal(decoded.GetType(), reread.GetType());
        JsonElement before = JsonSerializer.SerializeToElement(Value(decoded), ProjectionOptions);
        JsonElement after = JsonSerializer.SerializeToElement(Value(reread), ProjectionOptions);
        Assert.True(JsonElement.DeepEquals(before, after), $"{fixture}: fresh bytes decode to a different value.");
        bool identical = bytes.Span.SequenceEqual(rebuilt.Raw.Span);
        if (Differences.Value.TryGetValue(fixture, out string[]? pin))
        {
            string classes = X8WireDifferences.Classify(bytes, rebuilt.Raw);
            Assert.False(identical || classes.Length == 0 || classes.StartsWith("unexplained", StringComparison.Ordinal), $"{fixture}: obsolete or unexplained pin ({classes}).");
            Assert.Equal(pin[1], Sha(bytes.Span));
            Assert.Equal(pin[2], Sha(rebuilt.Raw.Span));
            Assert.Equal(pin[3], classes);
        }
        else
        {
            Assert.True(identical, $"{fixture}: unpinned fresh encoding ({Sha(bytes.Span)}/{Sha(rebuilt.Raw.Span)}).");
        }
        return rebuilt.Raw;
    }

    private static PropertyInfo? FactoryProperty(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        return type.GetProperty(name, flags) ?? (name.EndsWith("Value", StringComparison.Ordinal) ? type.GetProperty(name[..^5], flags) : null);
    }

    private static MethodInfo Factory(Type type)
    {
        MethodInfo[] candidates = [.. type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "Create" && method.ReturnType == type && method.GetParameters().All(parameter =>
                parameter.Name == "constrIndex" || FactoryProperty(type, parameter.Name!)?.PropertyType == parameter.ParameterType))];
        Assert.True(candidates.Length == 1, $"No unique matching Create for {type.FullName}: {candidates.Length}.");
        return candidates[0];
    }

    private static object? Field(ICborType value, ParameterInfo parameter) => parameter.Name == "constrIndex"
        ? ConstructorAlternative(value)
        : FactoryProperty(value.GetType(), parameter.Name!)!.GetValue(value);

    private static int ConstructorAlternative(ICborType value)
    {
        if (value is not PlutusConstr)
        {
            return value.ConstrIndex;
        }
        CborReader reader = new(value.Raw.Span);
        Assert.True(reader.TryReadSemanticTag(out ulong tag));
        // Compact forms expose the wire tag; tag 102 exposes the logical alternative.
        return tag switch
        {
            102 => value.ConstrIndex,
            >= 121 and <= 127 => (int)tag - 121,
            >= 1280 and <= 1400 => (int)tag - 1280 + 7,
            _ => throw new InvalidOperationException($"Unsupported Plutus constructor tag {tag}.")
        };
    }

    private static object? Value(object? value) => value switch
    {
        CborEncodedValue encoded => Convert.ToHexString(encoded.Value.Span),
        CborAny opaque => Convert.ToHexString(opaque.Raw.Span),
        ICborType list when IsCborList(list.GetType()) => new
        {
            cbor = list.GetType().FullName,
            list.IsIndefinite,
            HasSetTag = (bool)list.GetType().GetProperty(nameof(CborList<>.HasSetTag))!.GetValue(list)!,
            items = ((IEnumerable)list).Cast<object?>().Select(Value).ToArray()
        },
        ICborType cbor => new { cbor = cbor.GetType().FullName, fields = Factory(cbor.GetType()).GetParameters().ToDictionary(parameter => parameter.Name!, parameter => Value(Field(cbor, parameter)), StringComparer.Ordinal) },
        ReadOnlyMemory<byte> memory => Convert.ToHexString(memory.Span),
        byte[] bytes => Convert.ToHexString(bytes),
        IDictionary map => map.Keys.Cast<object>().Select(key => new { key = Value(key), value = Value(map[key]) })
            .OrderBy(entry => JsonSerializer.Serialize(entry.key, ProjectionOptions), StringComparer.Ordinal).ToArray(),
        IList list => list.Cast<object?>().Select(Value).ToArray(),
        not null when IsPair(value.GetType()) => new { key = Value(value.GetType().GetProperty("Key")!.GetValue(value)), value = Value(value.GetType().GetProperty("Value")!.GetValue(value)) },
        _ => value
    };

    private static bool IsCborList(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CborList<>);

    private static CborList<T> RebuildCborList<T>(CborList<T> list)
    {
        T[] items = new T[list.Count];
        int index = 0;
        foreach (T item in list)
        {
            items[index++] = (T)Rebuild(item)!;
        }
        return CborList.Create(items, list.IsIndefinite, list.HasSetTag);
    }

    private static bool IsPair(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    // Rebuild bottom-up. Create takes materialized children, not the original preserved bytes.
    private static object? Rebuild(object? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is byte[] bytes)
        {
            return bytes;
        }
        if (value is CborEncodedValue encoded)
        {
            // The enclosing writer writes tag 24 and the byte-string framing from the payload.
            return new CborEncodedValue(encoded.Value);
        }
        if (value is CborAny opaque)
        {
            // The entire wire item, rather than a decoded property, is the value of CborAny.
            return CborAny.FromRaw(opaque.Raw);
        }
        Type type = value.GetType();
        if (value is ICborType cbor)
        {
            try
            {
                if (IsCborList(type))
                {
                    return typeof(FreshReencodeTests).GetMethod(nameof(RebuildCborList), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(type.GetGenericArguments()[0]).Invoke(null, [value]);
                }
                MethodInfo create = Factory(type);
                object?[] arguments = [.. create.GetParameters().Select(parameter => Rebuild(Field(cbor, parameter)))];
                return create.Invoke(null, arguments);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        if (value is IDictionary map)
        {
            IDictionary rebuilt = (IDictionary)Activator.CreateInstance(type)!;
            foreach (DictionaryEntry entry in map)
            {
                rebuilt.Add(Rebuild(entry.Key)!, Rebuild(entry.Value));
            }
            return rebuilt;
        }
        if (value is IList list)
        {
            IList rebuilt = (IList)Activator.CreateInstance(type)!;
            foreach (object? item in list)
            {
                _ = rebuilt.Add(Rebuild(item));
            }
            return rebuilt;
        }
        if (IsPair(type))
        {
            return Activator.CreateInstance(type, Rebuild(type.GetProperty("Key")!.GetValue(value)),
                Rebuild(type.GetProperty("Value")!.GetValue(value)));
        }
        return value;
    }
}
