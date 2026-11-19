using System.Buffers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

/// <summary>
/// Tests for Shelley, Allegra, and Mary era blocks adopted from Pallas test fixtures.
/// All blocks are in era-tagged format [era_number, block_body] sourced from
/// https://github.com/txpipe/pallas/tree/main/test_data.
/// </summary>
public class EraTests
{
    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Theory]
    [InlineData(8UL)]
    [InlineData(255UL)]
    [InlineData(ulong.MaxValue)]
    public void UnknownBlockTagFailsBeforePayloadMaterialization(ulong tag)
    {
        byte[] raw = EnvelopeBytes(tag, BlockBody());
        UnknownEraException exception = Assert.Throws<UnknownEraException>(() => CborSerializer.Deserialize<BlockWithEra>(raw));
        AssertUnknown(exception.Value, tag, EraEnvelope.Block, raw);
        Assert.Null(exception.Point);
        _ = Assert.Throws<UnknownEraException>(() => BlockWithEra.Read(raw));
        _ = Assert.Throws<UnknownEraException>(() => CborSerializer.Deserialize<BlockWithEra>(raw, out _));
        AssertRuntimeReadersReject(raw, typeof(BlockWithEra));
        AssertRuntimeReadersReject(raw, typeof(BlockWithEra?));
        (ulong Slot, ReadOnlyMemory<byte> Hash) point = (42UL, new byte[32]);
        UnknownEraException contextual = Assert.Throws<UnknownEraException>(() => BlockWithEra.Read(raw, point));
        Assert.Equal(point, contextual.Point);
        byte[] preserved = [.. raw];
        raw.AsSpan().Clear();
        Assert.Equal(preserved, exception.Raw.ToArray());
        Assert.Equal(tag, exception.WireTag);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(255UL)]
    [InlineData(ulong.MaxValue)]
    public void UnknownHeaderTagFailsBeforePayloadMaterialization(ulong tag)
    {
        byte[] raw = EnvelopeBytes(tag, HeaderBody());
        UnknownEraException exception = Assert.Throws<UnknownEraException>(() => CborSerializer.Deserialize<N2NBlockHeader>(raw));
        AssertUnknown(exception.Value, tag, EraEnvelope.Header, raw);
        Assert.Null(exception.Point);
        _ = Assert.Throws<UnknownEraException>(() => N2NBlockHeader.Read(raw));
        _ = Assert.Throws<UnknownEraException>(() => CborSerializer.Deserialize<N2NBlockHeader>(raw, out _));
        AssertRuntimeReadersReject(raw, typeof(N2NBlockHeader));
        AssertRuntimeReadersReject(raw, typeof(N2NBlockHeader?));
        (ulong Slot, ReadOnlyMemory<byte> Hash) point = (42UL, new byte[32]);
        UnknownEraException contextual = Assert.Throws<UnknownEraException>(() => N2NBlockHeader.Read(raw, point));
        Assert.Equal(point, contextual.Point);
        byte[] preserved = [.. raw];
        raw.AsSpan().Clear();
        Assert.Equal(preserved, exception.Raw.ToArray());
        Assert.Equal(tag, exception.WireTag);
    }

    [Theory]
    [InlineData(8UL)]
    [InlineData(255UL)]
    [InlineData(ulong.MaxValue)]
    public void UnknownBlockRawModeNeverReturnsADecodedBlock(ulong tag)
    {
        byte[] raw = EnvelopeBytes(tag, BlockBody());
        byte[] followed = [.. raw, 0xf6];
        (ulong Slot, ReadOnlyMemory<byte> Hash) point = (42UL, new byte[32]);
        UnknownEra value = BlockWithEra.ReadUnknownEra(followed, point);
        AssertUnknown(value, tag, EraEnvelope.Block, raw);
        Assert.Equal(point, value.Point);
        Assert.False((object)value is IBlock);
        Assert.Equal(raw, CborSerializer.Serialize(value));
        _ = Assert.Throws<ArgumentException>(() => BlockWithEra.ReadUnknownEra(EnvelopeBytes(7, BlockBody())));
        followed.AsSpan().Clear();
        Assert.Equal(raw, value.Raw.ToArray());
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(255UL)]
    [InlineData(ulong.MaxValue)]
    public void UnknownHeaderRawModeNeverReturnsADecodedHeader(ulong tag)
    {
        byte[] raw = EnvelopeBytes(tag, HeaderBody());
        (ulong Slot, ReadOnlyMemory<byte> Hash) point = (42UL, new byte[32]);
        UnknownEra value = N2NBlockHeader.ReadUnknownEra(raw, point);
        AssertUnknown(value, tag, EraEnvelope.Header, raw);
        Assert.Equal(point, value.Point);
        Assert.False((object)value is INetworkBlockHeader);
        Assert.Equal(raw, CborSerializer.Serialize(value));
        _ = Assert.Throws<ArgumentException>(() => N2NBlockHeader.ReadUnknownEra(EnvelopeBytes(6, HeaderBody())));
        byte[] preserved = [.. raw];
        raw.AsSpan().Clear();
        Assert.Equal(preserved, value.Raw.ToArray());
    }

    [Theory]
    [InlineData(typeof(BlockWithEra))]
    [InlineData(typeof(N2NBlockHeader))]
    public void SupportedEraConstantsMatchEveryUnionHint(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        FieldInfo? limit = type.GetField("MaxSupportedEraTag", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(limit);
        ulong maximum = Assert.IsType<ulong>(limit.GetRawConstantValue());
        ulong[] declared = [.. type.GetProperties()
            .SelectMany(property => property.GetCustomAttributes<CborUnionHintAttribute>())
            .Select(hint => checked((ulong)hint.DiscriminantValue)).Distinct().Order()];
        Assert.Equal(Enumerable.Range(0, checked((int)maximum + 1)).Select(value => (ulong)value), declared);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstEnvelopeReadAllocatesOnlyItsBoxedResult(bool header)
    {
        byte[] bytes = header ? EnvelopeBytes(1, HeaderBody()) : LoadEraFixture(EraFixture("shelley1.block"));
        Type type = header ? typeof(N2NBlockHeader) : typeof(BlockWithEra);
        Assert.Equal(72, AllocationAssert.MeasureCold(() => FirstReadAllocations(type, bytes)));
    }

    private static long FirstReadAllocations(Type type, ReadOnlyMemory<byte> bytes)
    {
        InternUserStrings(type.Assembly.Location);
        AssemblyLoadContext isolated = new(name: null, isCollectible: true);
        isolated.Resolving += (context, name) => name.Name is "Chrysalis.Codec" or "Chrysalis.Network"
            ? context.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(type.Assembly.Location)!, name.Name + ".dll"))
            : null;
        try
        {
            Assembly assembly = isolated.LoadFromAssemblyPath(type.Assembly.Location);
            Type fresh = assembly.GetType(type.FullName!, throwOnError: true)!;
            MethodInfo read = fresh.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
            ParameterExpression data = Expression.Parameter(typeof(ReadOnlyMemory<byte>));
            Func<ReadOnlyMemory<byte>, object> invoke = Expression.Lambda<Func<ReadOnlyMemory<byte>, object>>(
                Expression.Convert(Expression.Call(read, data), typeof(object)), data).Compile();
            // Prepare the entry point without calling it; the measurement is still the first decode.
            RuntimeHelpers.PrepareMethod(read.MethodHandle);
            object? result = null;
            long allocated = AllocationAssert.MeasureOnce(() => result = invoke(bytes));
            GC.KeepAlive(result);
            return allocated;
        }
        finally
        {
            isolated.Unload();
        }
    }

    private static void InternUserStrings(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream, PEStreamOptions.LeaveOpen);
        MetadataReader metadata = pe.GetMetadataReader();
        for (UserStringHandle handle = metadata.GetNextHandle(default(UserStringHandle));
            !handle.IsNil;
            handle = metadata.GetNextHandle(handle))
        {
            _ = string.Intern(metadata.GetUserString(handle));
        }
    }

    [Theory]
    [InlineData("9FTTF600FF")]
    [InlineData("9FTTFF")]
    [InlineData("83TTF600")]
    [InlineData("81TTF6")]
    [InlineData("80TTF6")]
    [InlineData("80")]
    [InlineData("9FFF")]
    public void SupportedEnvelopesRejectWrongCardinalityInBothForms(string shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        foreach (bool header in new[] { false, true })
        {
            byte[] bytes = Convert.FromHexString(shape.Replace("TT", header ? "06" : "07", StringComparison.Ordinal));
            _ = Assert.Throws<CborDecodeException>(() => ReadEraEnvelope(bytes, header, false));
        }
    }

    public static IEnumerable<object[]> MalformedUnknownEnvelopes()
    {
        string[] shapes = ["9FTTFF", "9FTTF600FF", "9FTTF6", "81TT", "83TTF600", "82TT", "82TTFF"];
        foreach (string shape in shapes)
        {
            foreach (bool header in new[] { false, true })
            {
                foreach (bool rawMode in new[] { false, true })
                {
                    yield return [header, rawMode, shape.Replace("TT", header ? "07" : "08", StringComparison.Ordinal)];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MalformedUnknownEnvelopes))]
    public void MalformedUnknownEnvelopesAreNotReportedAsUnprocessedValues(bool header, bool rawMode, string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        Exception? exception = Record.Exception(() => _ = ReadEraEnvelope(bytes, header, rawMode));
        Assert.NotNull(exception);
        Assert.IsNotType<UnknownEraException>(exception);
    }

    [Theory]
    [InlineData(false, "9F08F6FF")]
    [InlineData(true, "9F07F6FF")]
    [InlineData(false, "8208F6")]
    [InlineData(true, "8207F6")]
    public void ValidUnknownEnvelopesRetainTheCompleteEnvelopeWithoutTrailingInput(bool header, string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        byte[] followed = [.. bytes, 0x00];
        UnknownEraException exception = Assert.Throws<UnknownEraException>(() => ReadEraEnvelope(followed, header, false));
        Assert.Equal(bytes, exception.Raw.ToArray());
        UnknownEra raw = Assert.IsType<UnknownEra>(ReadEraEnvelope(followed, header, true));
        Assert.Equal(bytes, raw.Raw.ToArray());
    }

    private static ICborType ReadEraEnvelope(byte[] bytes, bool header, bool rawMode)
    {
        if (rawMode)
        {
            return header ? N2NBlockHeader.ReadUnknownEra(bytes) : BlockWithEra.ReadUnknownEra(bytes);
        }
        return header ? N2NBlockHeader.Read(bytes) : BlockWithEra.Read(bytes);
    }

    [Theory]
    [InlineData("shelley1.block", typeof(AlonzoCompatibleBlock))]
    [InlineData("preprod-conway.block.cbor", typeof(ConwayBlock))]
    public void SupportedEnvelopesRetainGoldenBytesAndDistinctWireNumberings(string file, Type blockType)
    {
        JsonElement fixture = EraFixture(file);
        byte[] bytes = LoadEraFixture(fixture);
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(fixture.GetProperty("blockTag").GetInt32(), block.EraNumber);
        Assert.IsType(blockType, block.Block);
        Assert.Equal(bytes, CborSerializer.Serialize(block));
        byte[] headerBytes = EnvelopeBytes(fixture.GetProperty("headerTag").GetUInt64(), HeaderBody(bytes));
        N2NBlockHeader header = CborSerializer.Deserialize<N2NBlockHeader>(headerBytes);
        Assert.Equal(fixture.GetProperty("headerTag").GetInt32(), header.EraTag);
        _ = Assert.IsType<ShelleyN2NHeader>(header.Header);
        Assert.Equal(headerBytes, CborSerializer.Serialize(header));
    }

    private static JsonElement EraFixture(string file)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine("TestData", "unknown-era-manifest.json")));
        return manifest.RootElement.EnumerateArray().Single(entry => entry.GetProperty("path").GetString() == file).Clone();
    }

    private static byte[] LoadEraFixture(JsonElement fixture)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", fixture.GetProperty("path").GetString()!));
        Assert.Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return fixture.GetProperty("binary").GetBoolean() ? bytes : Convert.FromHexString(System.Text.Encoding.UTF8.GetString(bytes).Trim());
    }

    [Theory]
    [InlineData(typeof(ConwayBlock))]
    public void NonEraErrorsKeepExistingWrapping(Type type)
    {
        byte[] malformed = [0xff];
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<ConwayBlock>(malformed));
        _ = Assert.IsType<SAIB.Cbor.CborException>(error.InnerException);
        _ = Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.Read(malformed, type));
        _ = Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.ReadAnyWithConsumed(malformed, type, out _));
    }

    private static void AssertRuntimeReadersReject(byte[] raw, Type type)
    {
        _ = Assert.Throws<UnknownEraException>(() => GenericSerializationUtil.Read(raw, type));
        _ = Assert.Throws<UnknownEraException>(() => GenericSerializationUtil.ReadAnyWithConsumed(raw, type, out _));
    }

    private static void AssertUnknown(UnknownEra value, ulong tag, EraEnvelope envelope, byte[] raw)
    {
        Assert.Equal(tag, value.WireTag);
        Assert.Equal(envelope, value.Envelope);
        Assert.Equal(raw, value.Raw.ToArray());
    }

    private static ReadOnlyMemory<byte> BlockBody()
    {
        CborReader reader = new(LoadEraFixture(EraFixture("shelley1.block")));
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        _ = reader.ReadUInt64();
        return reader.ReadDataItem().ToArray();
    }

    private static ReadOnlyMemory<byte> HeaderBody() => HeaderBody(LoadEraFixture(EraFixture("shelley1.block")));

    private static ReadOnlyMemory<byte> HeaderBody(byte[] bytes)
    {
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(bytes);
        ReadOnlyMemory<byte> header = block.Block switch
        {
            AlonzoCompatibleBlock shelley => shelley.Header.Raw,
            ConwayBlock conway => conway.Header.Raw,
            _ => throw new InvalidOperationException("The fixture is not a Shelley or Conway block.")
        };
        ArrayBufferWriter<byte> output = new();
        CborWriter writer = new(output);
        writer.WriteSemanticTag(24);
        writer.WriteByteString(header.Span);
        return output.WrittenMemory;
    }

    private static byte[] EnvelopeBytes(ulong tag, ReadOnlyMemory<byte> body)
    {
        ArrayBufferWriter<byte> prefix = new();
        CborWriter writer = new(prefix);
        writer.WriteBeginArray(2);
        writer.WriteUInt64(tag);
        return [.. prefix.WrittenSpan, .. body.Span];
    }

    // --- Deserialization ---

    [Fact]
    public void DeserializeShelleyBlock()
    {
        byte[] cborRaw = LoadTestBlock("shelley1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.Equal(2, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    [Fact]
    public void DeserializeAllegraBlock()
    {
        byte[] cborRaw = LoadTestBlock("allegra1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.Equal(3, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    [Fact]
    public void DeserializeMaryBlock()
    {
        byte[] cborRaw = LoadTestBlock("mary1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.Equal(4, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    // --- Isomorphic round-trips ---

    [Fact]
    public void ShelleyBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("shelley1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void AllegraBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("allegra1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void MaryBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("mary1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }
}
