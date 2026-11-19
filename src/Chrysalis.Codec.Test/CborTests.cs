using System.Buffers;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Xunit.Abstractions;

namespace Chrysalis.Codec.Test;


internal record TestRecord(string P, int I);

// Test record with required fields - all fields must be present
[CborSerializable]
[CborList]
public readonly partial record struct PersonRequired : ICborType
{
    [CborOrder(0)] public partial int Id { get; }
    [CborOrder(1)] public partial string Name { get; }
    [CborOrder(2)] public partial int Age { get; }
}

// Test record with nullable fields - fields can be missing
[CborSerializable]
[CborList]
public readonly partial record struct PersonOptional : ICborType
{
    [CborOrder(0)] public partial int? Id { get; }
    [CborOrder(1)] public partial string? Name { get; }
    [CborOrder(2)] public partial int? Age { get; }
}


[CborSerializable, CborList]
public partial record TypedChecked([CborOrder(0)] int Value) : CborRecord;

[CborSerializable, CborList]
public partial record TypedShape([CborOrder(0), CborIndefinite] List<int> Value) : CborRecord;

public readonly record struct TypedCheckedValidator : ICborValidator<TypedChecked>
{
    public bool Validate(TypedChecked value) => value is not null && value.Value >= 0;
}

[CborSerializable, CborList]
public partial record TypedCollections(
    [CborOrder(0)] List<TypedChecked> Items,
    [CborOrder(1)] Dictionary<int, TypedChecked> Entries) : CborRecord;

[CborSerializable]
public readonly partial record struct TypedLazyMap : ICborType
{
    public partial Dictionary<int, TypedChecked> Value { get; }
}

[CborSerializable, CborList]
public partial record TypedMapShape([CborOrder(0), CborDefinite] Dictionary<int, int> Value) : CborRecord;

[CborSerializable, CborList]
public readonly partial record struct ChunkedByteList : ICborType
{
    [CborOrder(0)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>> Items { get; }
}

[CborSerializable, CborUnion]
public partial interface IX6DepthOne : ICborType;
[CborSerializable, CborList]
public partial record X6DepthOneA([CborOrder(0)] int Index, [CborOrder(1)] X6TrialLeaf Value) : CborRecord, IX6DepthOne;
[CborSerializable, CborList]
public partial record X6DepthOneB([CborOrder(0)] int Index, [CborOrder(1)] string Value) : CborRecord, IX6DepthOne;

[CborSerializable, CborList]
public partial record X6DepthOneC([CborOrder(0)] int Index, [CborOrder(1), CborIndefinite] List<int> Value) : CborRecord, IX6DepthOne;

[CborSerializable, CborUnion]
public partial interface IX6DepthTwo : ICborType;
[CborSerializable, CborList]
public partial record X6DepthTwoA([CborOrder(0)] int Index, [CborOrder(1)] X6TrialMiddle Value) : CborRecord, IX6DepthTwo;
[CborSerializable, CborList]
public partial record X6DepthTwoB([CborOrder(0)] int Index, [CborOrder(1)] X6TrialText Value) : CborRecord, IX6DepthTwo;

[CborSerializable, CborList]
public partial record X6TrialLeaf([CborOrder(0)] int Value) : CborRecord;
[CborSerializable, CborList]
public partial record X6TrialMiddle([CborOrder(0)] X6TrialLeaf Value) : CborRecord;
[CborSerializable, CborList]
public partial record X6TrialText([CborOrder(0)] string Value) : CborRecord;

// Paired source-generated models use the same field types for the eager and deferred paths.
public interface IX30WireValue
{
    object? Unwrap();
}

public interface IX30WireValue<out T> : IX30WireValue
{
    T Value { get; }
    object? IX30WireValue.Unwrap() => Value;
}

[CborSerializable]
public partial record X30EagerArray<T>(List<T> Value) : CborRecord, IX30WireValue<List<T>>;
[CborSerializable]
public readonly partial record struct X30LazyArray<T> : ICborType, IX30WireValue<List<T>>
{
    public partial List<T> Value { get; }
}

[CborSerializable]
public partial record X30EagerMap<TKey, TValue>(Dictionary<TKey, TValue> Value) : CborRecord, IX30WireValue<Dictionary<TKey, TValue>> where TKey : notnull;
[CborSerializable]
public readonly partial record struct X30LazyMap<TKey, TValue> : ICborType, IX30WireValue<Dictionary<TKey, TValue>> where TKey : notnull
{
    public partial Dictionary<TKey, TValue> Value { get; }
}

[CborSerializable, CborList]
public partial record X30EagerListTail<T>([CborOrder(0)] uint Marker, [CborOrder(1)] T Value) : CborRecord, IX30WireValue<T>;
[CborSerializable, CborList]
public readonly partial record struct X30LazyListTail<T> : ICborType, IX30WireValue<T>
{
    [CborOrder(0)] public partial uint Marker { get; }
    [CborOrder(1)] public partial T Value { get; }
}

[CborSerializable, CborTag(121)]
public partial record X30EagerTag121<T>(T Value) : CborRecord, IX30WireValue<T>;
[CborSerializable, CborTag(121)]
public readonly partial record struct X30LazyTag121<T> : ICborType, IX30WireValue<T>
{
    public partial T Value { get; }
}

[CborSerializable, CborTag(259)]
public partial record X30EagerTag259<T>(T Value) : CborRecord, IX30WireValue<T>;
[CborSerializable, CborTag(259)]
public readonly partial record struct X30LazyTag259<T> : ICborType, IX30WireValue<T>
{
    public partial T Value { get; }
}

[CborSerializable, CborTag(60000)]
public partial record X30EagerTag60000<T>(T Value) : CborRecord, IX30WireValue<T>;
[CborSerializable, CborTag(60000)]
public readonly partial record struct X30LazyTag60000<T> : ICborType, IX30WireValue<T>
{
    public partial T Value { get; }
}

[CborSerializable]
public partial record X30EagerBytes(ReadOnlyMemory<byte> Value) : CborRecord, IX30WireValue<ReadOnlyMemory<byte>>;
[CborSerializable]
public readonly partial record struct X30LazyBytes : ICborType, IX30WireValue<ReadOnlyMemory<byte>>
{
    public partial ReadOnlyMemory<byte> Value { get; }
}

[CborSerializable]
public partial record X30EagerEncoded(CborEncodedValue Value) : CborRecord, IX30WireValue<CborEncodedValue>;
[CborSerializable]
public readonly partial record struct X30LazyEncoded : ICborType, IX30WireValue<CborEncodedValue>
{
    public partial CborEncodedValue Value { get; }
}

[CborSerializable]
public readonly partial record struct X30LazyPrimitive<T> : ICborType, IX30WireValue<T>
{
    public partial T Value { get; }
}

[CborSerializable]
public partial record X32EagerAny(CborAny Value) : CborRecord;

[CborSerializable]
public partial record X32EagerBytesList(List<ReadOnlyMemory<byte>> Value) : CborRecord, IX30WireValue<List<ReadOnlyMemory<byte>>>;
[CborSerializable]
public readonly partial record struct X32LazyBytesList : ICborType, IX30WireValue<List<ReadOnlyMemory<byte>>>
{
    public partial List<ReadOnlyMemory<byte>> Value { get; }
}

[CborSerializable]
public partial record X32EagerBytesMap(Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> Value) : CborRecord, IX30WireValue<Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>;
[CborSerializable]
public readonly partial record struct X32LazyBytesMap : ICborType, IX30WireValue<Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>
{
    public partial Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> Value { get; }
}

[CborSerializable]
public partial record X32EagerIntList(List<int> Value) : CborRecord, IX30WireValue<List<int>>;
[CborSerializable]
public readonly partial record struct X32LazyIntList : ICborType, IX30WireValue<List<int>>
{
    public partial List<int> Value { get; }
}

[CborSerializable]
public partial record X32EagerIntBoolMap(Dictionary<int, bool> Value) : CborRecord, IX30WireValue<Dictionary<int, bool>>;
[CborSerializable]
public readonly partial record struct X32LazyIntBoolMap : ICborType, IX30WireValue<Dictionary<int, bool>>
{
    public partial Dictionary<int, bool> Value { get; }
}

public class CborTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("18", 1)]
    public void TypedDecodeTruncationRetainsReaderOffset(string hex, int offset)
    {
        byte[] data = Convert.FromHexString(hex);
        CborDecodeException error = AssertDecodeError(() => CborSerializer.Deserialize<int>(data), offset, "Int32");
        _ = Assert.IsType<SAIB.Cbor.CborException>(error.InnerException);
        Assert.Equal("integer", error.Expected);
        Assert.Equal("end of input", error.Actual);
        _ = AssertDecodeError(() => CborSerializer.Deserialize<int>(data, out _), offset, "Int32");
        AssertRuntimeDecodeErrors(data, typeof(int), offset, "Int32");
    }

    [Fact]
    public void TypedDecodeWrongMajorTypeNamesLazyField()
    {
        PersonRequired value = PersonRequired.Read(Convert.FromHexString("836178616100"));
        CborDecodeException error = AssertDecodeError(() => _ = value.Id, 2, "PersonRequired.Id");
        Assert.Equal("text string", error.Actual);
        Assert.Equal("integer", error.Expected);
        Assert.Equal("CBOR Chrysalis.Codec.Test.PersonRequired.Id at byte 2: expected integer; actual text string.", error.Message);
        _ = AssertDecodeError(() => TypedChecked.Read(Convert.FromHexString("816178")), 2, "TypedChecked.Value");
        CborDecodeException shape = AssertDecodeError(() => TypedShape.Read(Convert.FromHexString("8180")), 1, "TypedShape.Value");
        Assert.Equal("Chrysalis.Codec.Test.TypedShape.Value", shape.TypePath);
        Assert.Equal("indefinite array", shape.Expected);
        Assert.Equal("definite array", shape.Actual);
    }

    [Theory]
    [InlineData("d87a80", "122")]
    [InlineData("80", "array")]
    public void TypedDecodeInvalidTagHasStructuredContext(string hex, string actual)
    {
        byte[] data = Convert.FromHexString(hex);
        CborDecodeException error = AssertDecodeError(() => PlutusVoid.Read(data), 0, "PlutusVoid");
        Assert.Equal("tag 121", error.Expected);
        Assert.Equal(actual, error.Actual);
        _ = AssertDecodeError(() => CborSerializer.Deserialize<PlutusVoid>(data), 0, "PlutusVoid");
    }

    [Theory]
    [InlineData("fc", 0)]
    [InlineData("1c", 0)]
    public void TypedDecodeUnionFailureIsNotAnInvocationWrapper(string hex, int offset)
    {
        byte[] data = Convert.FromHexString(hex);
        AssertRuntimeDecodeErrors(data, typeof(IPlutusData), offset, "IPlutusData");
    }

    [Fact]
    public void TypedValidationIsPreservedByReadAndWriteEntryPoints()
    {
        byte[] data = Convert.FromHexString("8120");
        Exception error = AssertDecodeError(() => TypedChecked.Read(data), 2, "TypedChecked");
        Assert.Equal("CborValidationException", error.GetType().Name);
        Assert.Equal("CborValidationException", AssertDecodeError(() => CborSerializer.Deserialize<TypedChecked>(data), 2, "TypedChecked").GetType().Name);
        TypedChecked invalid = new(-1);
        Assert.Equal("CborValidationException", AssertDecodeError(() => CborSerializer.Serialize(invalid), -1, "TypedChecked").GetType().Name);
        Assert.Equal("CborValidationException", AssertDecodeError(() => CborSerializer.SerializeToMemory(invalid), -1, "TypedChecked").GetType().Name);
        Assert.Equal("CborValidationException", AssertDecodeError(() => CborSerializer.GetEncodedLength(invalid), -1, "TypedChecked").GetType().Name);
    }

    [Fact]
    public void TypedDecodeUnsupportedTypeAndSuccessAllocationContract()
    {
        _ = AssertDecodeError(() => CborSerializer.Deserialize<DateTime>(new byte[] { 0 }), 0, "DateTime");
        byte[] malformed = [0xff];
        _ = Assert.Throws<SAIB.Cbor.CborException>(() => ConwayBlock.Read(malformed));
        CborDecodeException error = AssertDecodeError(() => CborSerializer.Deserialize<ConwayBlock>(malformed), 1, "ConwayBlock");
        _ = Assert.IsType<SAIB.Cbor.CborException>(error.InnerException);
        Assert.Equal("array", error.Expected);
        UnknownEraException unknown = new(new UnknownEra(8, EraEnvelope.Block, new byte[] { 0x98, 2, 8, 0x80 }));
        _ = Assert.IsAssignableFrom<CborDecodeException>(unknown);
        Assert.Equal(2, unknown.Offset);
        Assert.Equal("CBOR Block at byte 2: expected supported wire era tag; actual 8.", unknown.Message);
        byte[] data = Convert.FromHexString("8301616102");
        int sum = 0;
        long allocated = AllocationAssert.Measure(
            () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _ = PersonRequired.Read(data).Id;
                }
            },
            () =>
            {
                sum = 0;
                for (int i = 0; i < 1_000; i++)
                {
                    sum += PersonRequired.Read(data).Id;
                }
            },
            () => Assert.Equal(1_000, sum));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PlutusUnionTrialMissesDoNotIncreaseAllocations()
    {
        const int iterations = 1_000;
        // The exact base (10d89b1d) allocates 1,200 bytes for this warmed decode.
        const int baselineBytesPerRead = 1_200;
        byte[] data = [0x01];
        int matches = 0;
        long bytes = AllocationAssert.Measure(
            () =>
            {
                for (int i = 0; i < 500; i++)
                {
                    _ = IPlutusData.Read(data, out _);
                }
            },
            () =>
            {
                matches = 0;
                for (int i = 0; i < iterations; i++)
                {
                    if (IPlutusData.Read(data, out int consumed) is PlutusUint64 && consumed == 1)
                    {
                        matches++;
                    }
                }
            },
            () => Assert.Equal(iterations, matches));
        Assert.InRange(bytes, 0, baselineBytesPerRead * iterations);
    }

    [Theory]
    [InlineData("42414241CC", "4142", 3)]
    [InlineData("5F41AA41BBFF41CC", "AABB", 6)]
    public void CachedByteReadersAndListPreserveChunkedItems(string hex, string first, int consumed)
    {
        byte[] data = Convert.FromHexString(hex);
        Assert.Equal(first, Convert.ToHexString(CborPrimitiveReaders.ReadByteArray(data, out int arraySize)));
        Assert.Equal(consumed, arraySize);
        Assert.Equal(first, Convert.ToHexString(CborPrimitiveReaders.ReadByteMemory(data, out int memorySize).Span));
        Assert.Equal(consumed, memorySize);

        ChunkedByteList list = ChunkedByteList.Read(Convert.FromHexString("8182" + hex));
        CborListEnumerator<ReadOnlyMemory<byte>> values = list.Items.GetValue().GetEnumerator();
        Assert.True(values.MoveNext());
        Assert.Equal(first, Convert.ToHexString(values.Current.Span));
        Assert.True(values.MoveNext());
        Assert.Equal("CC", Convert.ToHexString(values.Current.Span));
        Assert.False(values.MoveNext());
    }

    [Fact]
    public void TypedRuntimeDispatchPropagatesNonDecodeFailures() =>
        AssertRuntimePropagates(typeof(RuntimeReadFailure), RuntimeReadFailure.Causes);

    private static void AssertRuntimePropagates(Type type, Exception[] causes)
    {
        for (byte i = 0; i < causes.Length; i++)
        {
            byte[] data = [i];
            Assert.Same(causes[i], Record.Exception(() => GenericSerializationUtil.Read(data, type)));
            Assert.Same(causes[i], Record.Exception(() => GenericSerializationUtil.ReadAnyWithConsumed(data, type, out _)));
        }
    }

    private sealed class RuntimeReadFailure
    {
        public static Exception[] Causes { get; } = [new OperationCanceledException(), Activator.CreateInstance<OutOfMemoryException>(), new TimeoutException()];
        public static RuntimeReadFailure Read(ReadOnlyMemory<byte> data) => throw Causes[data.Span[0]];
        public static RuntimeReadFailure Read(ReadOnlyMemory<byte> data, out int bytesConsumed) => throw Causes[data.Span[0]];
    }

    [Theory]
    [InlineData("828281018120A0", "TypedCollections.Items", 6)]
    [InlineData("8280A20181011901008120", "TypedCollections.Entries", 11)]
    public void TypedDecodeRebasesEagerCollectionErrors(string hex, string path, int offset)
    {
        CborDecodeException error = AssertDecodeError(() => TypedCollections.Read(Convert.FromHexString(hex)), offset, path);
        _ = Assert.IsType<CborValidationException>(error);
        Assert.Contains("TypedChecked", error.TypePath, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedDecodeRebasesLazyMapValueAfterMultibyteKey()
    {
        TypedLazyMap map = TypedLazyMap.Read(Convert.FromHexString("A20181011901008120"));
        _ = Assert.IsType<CborValidationException>(AssertDecodeError(() => _ = map.Value, 9, "TypedLazyMap.Value"));
    }

    [Fact]
    public void TypedDecodeReportsTheObservedArity()
    {
        CborDecodeException arity = AssertDecodeError(() => ByronMainBlock.Read(Convert.FromHexString("82F6F6")), 0, "ByronMainBlock");
        Assert.Equal("array length 3", arity.Expected);
        Assert.Equal("array length 2", arity.Actual);
    }

    [Fact]
    public void TypedDecodeReportsTheObservedMapForm()
    {
        CborDecodeException form = AssertDecodeError(() => TypedMapShape.Read(Convert.FromHexString("81BFFF")), 1, "TypedMapShape.Value");
        Assert.Equal("definite map", form.Expected);
        Assert.Equal("indefinite map", form.Actual);
    }

    [Fact]
    public void TypedDecodeReportsTheKindOfTheFailingChunk()
    {
        CborDecodeException chunk = AssertDecodeError(() => CborSerializer.Deserialize<byte[]>(Convert.FromHexString("5F416101FF")), 4, "Byte[]");
        Assert.Equal("byte string", chunk.Expected);
        Assert.Equal("unsigned integer", chunk.Actual);
    }

    [Fact]
    public void TypedDecodeGuardUsesOwningTypeAndWireExpectation()
    {
        CborDecodeException error = AssertDecodeError(() => PlutusList.Read(new byte[] { 1 }), 0, "PlutusList.Value");
        Assert.Equal("array", error.Expected);
        Assert.Equal("unsigned integer", error.Actual);
    }

    [Fact]
    public void UnionWriterRejectsNullArgument() =>
        Assert.Throws<ArgumentNullException>("data", () => IPlutusData.Write(new ArrayBufferWriter<byte>(), null!));

    [Theory]
    [InlineData(false, 192)]
    [InlineData(true, 256)]
    public void X7NestedUnionDispatchDoesNotAllocateOrThrowForRejectedCandidates(bool deep, int baselineBytesPerRead)
    {
        byte[] data = Convert.FromHexString(deep ? "8201816178" : "82016178");
        Func<bool> read = deep
            ? () => IX6DepthTwo.Read(data, out _) is X6DepthTwoB
            : () => IX6DepthOne.Read(data, out _) is X6DepthOneB;
        const int iterations = 1_000;
        int matches = 0;
        long bytes = AllocationAssert.Measure(
            () =>
            {
                for (int i = 0; i < 500; i++)
                {
                    _ = read();
                }
            },
            () =>
            {
                matches = 0;
                for (int i = 0; i < iterations; i++)
                {
                    if (read())
                    {
                        matches++;
                    }
                }
            },
            () => Assert.Equal(iterations, matches));

        int thread = Environment.CurrentManagedThreadId;
        int throws = 0;
        void CountThrow(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                throws++;
            }
        }
        AppDomain.CurrentDomain.FirstChanceException += CountThrow;
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                _ = read();
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= CountThrow;
        }
        output.WriteLine($"X7 nested dispatch depth {(deep ? 2 : 1)}: {bytes} bytes and {throws} throws across {iterations} reads.");
        Assert.True(bytes <= (long)baselineBytesPerRead * iterations,
            $"Expected at most {baselineBytesPerRead} bytes and no throws; observed {bytes} bytes and {throws} throws across {iterations} reads.");
        Assert.Equal(0, throws);
    }

    [Theory]
    [InlineData("F4", false, 1, "simple value or floating point")]
    [InlineData("F480", false, 1, "simple value or floating point")]
    [InlineData("F580", false, 1, "simple value or floating point")]
    [InlineData("617880", false, 2, "text string")]
    [InlineData("F680", true, 1, "simple value or floating point")]
    public void TypedDecodeDoesNotObserveTheItemAfterAFailedPrimitive(string hex, bool boolean, int offset, string actual)
    {
        byte[] data = Convert.FromHexString(hex);
        string path = boolean ? "System.Boolean" : "System.Single";
        string expected = boolean ? "boolean" : "floating point";
        CborDecodeException error = AssertExactDecodeError(() =>
        {
            if (boolean)
            {
                _ = CborSerializer.Deserialize<bool>(data);
            }
            else
            {
                _ = CborSerializer.Deserialize<float>(data);
            }
        }, offset, path, expected, actual);
        if (boolean)
        {
            Assert.Contains("Expected primitive", Assert.IsType<SAIB.Cbor.CborException>(error.InnerException).Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, "820181F6", "simple value or floating point")]
    [InlineData(true, "820181A0", "map")]
    public void X7TypedErrorNamesTheDeepestFieldWhenNoCandidateMatches(bool deep, string hex, string actual)
    {
        byte[] data = Convert.FromHexString(hex);
        string union = deep ? nameof(IX6DepthTwo) : nameof(IX6DepthOne);
        string path = $"Chrysalis.Codec.Test.{union}.Chrysalis.Codec.Test.{(deep ? "X6TrialText" : "X6TrialLeaf")}.Value";
        _ = AssertExactDecodeError(() =>
        {
            if (deep)
            {
                _ = IX6DepthTwo.Read(data);
            }
            else
            {
                _ = IX6DepthOne.Read(data);
            }
        }, 3, path, deep ? "text string" : "integer", actual);
    }

    public static TheoryData<string, string, string, string> X30WireFixtures()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "X30WireForms.json")));
        TheoryData<string, string, string, string> fixtures = [];
        foreach (JsonElement row in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString()!;
            string hex = row.GetProperty("hex").GetString()!;
            string sha256 = row.GetProperty("sha256").GetString()!;
            string golden = row.GetProperty("expected").GetRawText();
            fixtures.Add(id, hex, sha256, golden);
            if (id == "BytesRoot")
            {
                fixtures.Add("BytesRootPlutus", hex, sha256, golden);
            }
        }
        Assert.Equal(22, fixtures.Count);
        return fixtures;
    }

    [Theory]
    [MemberData(nameof(X30WireFixtures))]
    public void X30EagerAndLazyDecodeEveryWireForm(string id, string hex, string sha256, string golden)
    {
        byte[] wire = Convert.FromHexString(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(wire)));
        // A second CBOR item verifies the exact boundary of the first, including its break(s).
        byte[] withSuffix = [.. wire, 0x00];
        using JsonDocument expected = JsonDocument.Parse(golden);
        foreach (X30Read read in X30Readers(id))
        {
            object? value = read(withSuffix, out int consumed);
            Assert.Equal(wire.Length, consumed);
            object? target = id switch
            {
                "DefArrayList" or "IndefArrayList" or "BytesList" => ((IList)value!)[0],
                "DefArrayMap" or "DefMapMap" or "BytesMap" => ((IDictionary)value!).Values.Cast<object>().Single(),
                "IndefArrayMap" => ((IDictionary)value!).Keys.Cast<object>().Single(),
                "IndefMapMap" => ((IDictionary)value!).Values.Cast<object>().Last(),
                _ => value
            };
            JsonElement actual = JsonSerializer.SerializeToElement(X30Semantic(target));
            Assert.True(JsonElement.DeepEquals(expected.RootElement, actual), $"{id}: expected {golden}; actual {actual}.");
        }
    }

    private static object? X30Semantic(object? value) => value switch
    {
        IX30WireValue field => X30Semantic(field.Unwrap()),
        CborEncodedValue encoded => X30Semantic(encoded.Value),
        ReadOnlyMemory<byte> bytes => new { bytes = Convert.ToHexString(bytes.Span) },
        byte[] bytes => new { bytes = Convert.ToHexString(bytes) },
        IDictionary map => new { map = map.Keys.Cast<object>().Select(key => new[] { X30Semantic(key), X30Semantic(map[key]) }).ToArray() },
        IList list => list.Cast<object>().Select(X30Semantic).ToArray(),
        _ => value
    };

    private delegate object? X30Read(ReadOnlyMemory<byte> data, out int consumed);

    private static X30Read[] X30Pair<TEager, TLazy>() where TEager : IX30WireValue where TLazy : IX30WireValue =>
        [X30ReadModel<TEager>, X30ReadModel<TLazy>];

    private static object? X30ReadModel<T>(ReadOnlyMemory<byte> data, out int consumed) where T : IX30WireValue =>
        CborSerializer.Deserialize<T>(data, out consumed).Unwrap();

    private static X30Read[] X30Readers(string id) => id switch
    {
        "DefArrayRoot" or "IndefArrayRoot" => X30Pair<X30EagerArray<int>, X30LazyArray<int>>(),
        "DefArrayList" or "IndefArrayList" => X30Pair<X30EagerArray<X30EagerArray<int>>, X30LazyArray<X30LazyArray<int>>>(),
        "DefArrayMap" => X30Pair<X30EagerMap<string, X30EagerArray<string>>, X30LazyMap<string, X30LazyArray<string>>>(),
        "DefArrayTag" or "IndefArrayTag" => X30Pair<X30EagerTag121<X30EagerArray<int>>, X30LazyTag121<X30LazyArray<int>>>(),
        "IndefArrayMap" => X30Pair<X30EagerMap<X30EagerArray<byte[]>, X30EagerListTail<X30EagerTag121<X30EagerArray<ulong>>>>,
            X30LazyMap<X30LazyArray<byte[]>, X30EagerListTail<X30EagerTag121<X30EagerArray<ulong>>>>>(),
        "DefMapRoot" => X30Pair<X30EagerMap<int, byte[]>, X30LazyMap<int, byte[]>>(),
        "DefMapList" => X30Pair<X30EagerListTail<X30EagerMap<byte[], ulong>>, X30LazyListTail<X30LazyMap<byte[], ulong>>>(),
        "DefMapMap" => X30Pair<X30EagerMap<int, X30EagerMap<int, byte[]>>, X30LazyMap<int, X30LazyMap<int, byte[]>>>(),
        "DefMapTag" => X30Pair<X30EagerTag259<X30EagerMap<int, X30EagerMap<int, byte[]>>>, X30LazyTag259<X30LazyMap<int, X30LazyMap<int, byte[]>>>>(),
        "IndefMapRoot" => X30Pair<X30EagerMap<byte[], X30EagerMap<byte[], ulong>>, X30LazyMap<byte[], X30LazyMap<byte[], ulong>>>(),
        "IndefMapList" => X30Pair<X30EagerListTail<X30EagerMap<byte[], X30EagerMap<byte[], ulong>>>, X30LazyListTail<X30LazyMap<byte[], X30LazyMap<byte[], ulong>>>>(),
        "IndefMapMap" => X30Pair<X30EagerMap<byte[], X30EagerMap<byte[], ulong>>, X30LazyMap<byte[], X30LazyMap<byte[], ulong>>>(),
        "IndefMapTag" => X30Pair<X30EagerTag60000<X30EagerMap<byte[], X30EagerMap<byte[], ulong>>>, X30LazyTag60000<X30LazyMap<byte[], X30LazyMap<byte[], ulong>>>>(),
        "BytesRoot" => [X30ReadModel<X30EagerBytes>, X30ReadModel<X30LazyBytes>, CborSerializer.Deserialize<byte[]>, X30ReadModel<X30LazyPrimitive<byte[]>>],
        "BytesList" => X30Pair<X30EagerArray<X30EagerBytes>, X30LazyArray<X30LazyBytes>>(),
        "BytesMap" => X30Pair<X30EagerMap<int, X30EagerBytes>, X30LazyMap<int, X30LazyBytes>>(),
        "BytesTag" => X30Pair<X30EagerTag60000<X30EagerBytes>, X30LazyTag60000<X30LazyBytes>>(),
        "BytesRootPlutus" => [static (data, out consumed) => PlutusBoundedBytes.Read(data, out consumed).Value],
        "BytesEncoded" => X30Pair<X30EagerEncoded, X30LazyEncoded>(),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Missing reader pair for the fixture.")
    };

    [Fact]
    public void X30WrongTypeAfterValidListSibling()
    {
        byte[] data = Convert.FromHexString("A100820141FF"); // {0: [1, h'FF']}
        string child = $"{typeof(X30EagerArray<int>).FullName}.Chrysalis.Codec.Test.X30EagerArray<T>.Value.System.Int32";
        _ = AssertExactDecodeError(() => X30EagerMap<int, X30EagerArray<int>>.Read(data), 5,
            $"Chrysalis.Codec.Test.X30EagerMap<TKey, TValue>.Value.{child}", "integer", "byte string");
        X30LazyMap<int, X30EagerArray<int>> lazy = X30LazyMap<int, X30EagerArray<int>>.Read(data);
        _ = AssertExactDecodeError(() => _ = lazy.Value, 5,
            $"Chrysalis.Codec.Test.X30LazyMap<TKey, TValue>.Value.entry.{child}", "integer", "byte string");
    }

    [Fact]
    public void X30WrongTypeAfterMultibyteMapKey()
    {
        byte[] data = Convert.FromHexString("81A20001190100F6"); // [{0: 1, 256: null}]
        string child = $"{typeof(X30EagerMap<int, int>).FullName}.Chrysalis.Codec.Test.X30EagerMap<TKey, TValue>.Value.System.Int32";
        _ = AssertExactDecodeError(() => X30EagerArray<X30EagerMap<int, int>>.Read(data), 8,
            $"Chrysalis.Codec.Test.X30EagerArray<T>.Value.{child}", "integer", "simple value or floating point");
        X30LazyArray<X30EagerMap<int, int>> lazy = X30LazyArray<X30EagerMap<int, int>>.Read(data);
        _ = AssertExactDecodeError(() => _ = lazy.Value, 8,
            $"Chrysalis.Codec.Test.X30LazyArray<T>.Value.item.{child}", "integer", "simple value or floating point");
    }

    [Fact]
    public void X30TruncatedNestedByteString()
    {
        byte[] data = Convert.FromHexString("A1008142AA"); // {0: [bytes(2) with only AA]}
        string child = $"{typeof(X30EagerArray<byte[]>).FullName}.Chrysalis.Codec.Test.X30EagerArray<T>.Value.System.Byte[]";
        _ = AssertExactDecodeError(() => X30EagerMap<int, X30EagerArray<byte[]>>.Read(data), 4,
            $"Chrysalis.Codec.Test.X30EagerMap<TKey, TValue>.Value.{child}", "byte string", "end of input");
        // Lazy Read must first find the root's boundary. X6's facade wraps that scan failure.
        Type lazyType = typeof(X30LazyMap<int, X30EagerArray<byte[]>>);
        _ = AssertExactDecodeError(() => CborSerializer.Deserialize<X30LazyMap<int, X30EagerArray<byte[]>>>(data, out _), 4,
            lazyType.FullName!, lazyType.ToString(), "end of input");
    }

    public static TheoryData<string, string, string, string, int, string?, bool?> X32WireFixtures()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "X32WireForms.json")));
        TheoryData<string, string, string, string, int, string?, bool?> fixtures = [];
        foreach (JsonElement row in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            fixtures.Add(row.GetProperty("id").GetString()!, row.GetProperty("hex").GetString()!,
                row.GetProperty("sha256").GetString()!, row.GetProperty("expected").GetRawText(),
                row.TryGetProperty("payloadOffset", out JsonElement offset) ? offset.GetInt32() : -1,
                row.TryGetProperty("joined", out JsonElement joined) ? joined.GetString() : null,
                row.TryGetProperty("indefinite", out JsonElement indefinite) ? indefinite.GetBoolean() : null);
        }
        return fixtures;
    }

    [Theory]
    [MemberData(nameof(X32WireFixtures))]
    public void X32ChunkedPayloadsAndRedeemerSplices(string id, string hex, string hash, string golden, int payloadOffset, string? joined, bool? indefinite)
    {
        byte[] wire = Convert.FromHexString(hex);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(wire)));
        using JsonDocument expected = JsonDocument.Parse(golden);
        if (joined is not null)
        {
            // A minimal Conway block shell for the public resolver: only the input and witness are accessed.
            TransactionInput input = TransactionInput.Create(new byte[32], 0);
            ConwayBlock block = ConwayBlock.Read(Convert.FromHexString($"858081A30081{Convert.ToHexString(input.Raw.Span)}0180020081A105{hex}A080"));
            RedeemerEntry entry = Assert.IsType<RedeemerEntry>(input.Redeemer(block));
            Assert.Equal(joined, Convert.ToHexString(entry.Raw.Span));
            JsonElement values = expected.RootElement;
            Assert.Equal(values[0].GetInt32(), entry.Tag);
            Assert.Equal(values[1].GetUInt64(), entry.Index);
            Assert.Equal(values[2].GetString(), Convert.ToHexString(Assert.IsType<PlutusBoundedBytes>(entry.Data).Value.Span));
            Assert.Equal(values[3][0].GetUInt64(), entry.ExUnits.Mem);
            Assert.Equal(values[3][1].GetUInt64(), entry.ExUnits.Steps);
            return;
        }

        byte[] padded = [0x00, .. wire, 0x00];
        ReadOnlyMemory<byte> data = padded.AsMemory(1);
        if (indefinite is bool expectedIndefinite)
        {
            UnknownEra unknown = BlockWithEra.ReadUnknownEra(data);
            Assert.Equal(expected.RootElement[0].GetUInt64(), unknown.WireTag);
            Assert.True(wire.AsSpan().SequenceEqual(unknown.Raw.Span));
            Assert.Equal(expectedIndefinite, unknown.IsIndefinite);
            return;
        }
        if (id is "ByronTaggedArrayProof" or "ByronTaggedBytesProof")
        {
            BlockWithEra envelope = BlockWithEra.Read(data, out int consumed);
            Assert.Equal(wire.Length, consumed);
            if (expected.RootElement.GetBoolean())
            {
                ByronMainBlock block = Assert.IsType<ByronMainBlock>(envelope.Block);
                Assert.True(wire.AsSpan(2).SequenceEqual(block.Raw.Span));
            }
            else
            {
                _ = Assert.Throws<CborValidationException>(() => envelope.Block);
            }
            return;
        }
        foreach (X30Read read in X32Readers(id))
        {
            object? result = read(data, out int consumed);
            Assert.Equal(wire.Length, consumed);
            if (payloadOffset >= 0 && result is ReadOnlyMemory<byte> memory)
            {
                Assert.True(MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> view));
                Assert.Same(padded, view.Array);
                Assert.Equal(payloadOffset + 1, view.Offset);
            }
            JsonElement actual = JsonSerializer.SerializeToElement(X32Semantic(result));
            Assert.True(JsonElement.DeepEquals(expected.RootElement, actual), $"{id}: expected {golden}; actual {actual}.");
        }
    }

    private static object? X32Semantic(object? value) => value switch
    {
        CborEncodedValue encoded => X32Semantic(encoded.Value),
        ReadOnlyMemory<byte> bytes => Convert.ToHexString(bytes.Span),
        byte[] bytes => Convert.ToHexString(bytes),
        IDictionary map => map.Keys.Cast<object>().SelectMany(key => new[] { X32Semantic(key), X32Semantic(map[key]) }).ToArray(),
        IList list => list.Cast<object>().Select(X32Semantic).ToArray(),
        _ => value
    };

    private static X30Read[] X32Readers(string id) => id switch
    {
        "BareMemory" or "TaggedEmptyMemory" or "TaggedDefiniteMemory" => X30Pair<X30EagerBytes, X30LazyBytes>(),
        "TaggedMemory" => [X30ReadModel<X30EagerBytes>, X30ReadModel<X30LazyBytes>, CborSerializer.Deserialize<byte[]>,
            static (data, out consumed) => CborBytes.Read(data, out consumed).Value,
            static (data, out consumed) => PlutusBoundedBytes.Read(data, out consumed).Value],
        "ListMemory" => X30Pair<X32EagerBytesList, X32LazyBytesList>(),
        "MapMemory" => X30Pair<X32EagerBytesMap, X32LazyBytesMap>(),
        "EagerEncodedBare" or "EagerEncodedNestedTags" => [X30ReadModel<X30EagerEncoded>],
        "BigUintMagnitude" => [static (data, out consumed) => PlutusBigUint.Read(data, out consumed).Value],
        "ScriptBytes" => [static (data, out consumed) => PlutusV1Script.Read(data, out consumed).ScriptBytes],
        "DatumHash" => [static (data, out consumed) => DatumHashOption.Read(data, out consumed).DatumHash],
        "InlineDatum" => [static (data, out consumed) => InlineDatumOption.Read(data, out consumed).Data],
        "ScriptReference" => [static (data, out consumed) => PostAlonzoTransactionOutput.Read(data, out consumed).ScriptRef],
        "ScriptWitnesses" => [static (data, out consumed) => PostAlonzoTransactionWitnessSet.Read(data, out consumed).PlutusV1Scripts!.GetValue().Single()],
        "TaggedDefiniteText" => [CborSerializer.Deserialize<string>, static (data, out consumed) => CborString.Read(data, out consumed).Value],
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Missing X32 reader.")
    };

    [Theory]
    [InlineData("D8")]
    [InlineData("DC")]
    public void X32MalformedTagsKeepTheirExistingTypedError(string hex)
    {
        byte[] data = Convert.FromHexString(hex);
        _ = AssertDecodeError(() => CborBytes.Read(data), 0, "CborBytes.Value");
        _ = AssertDecodeError(() => CborString.Read(data), 0, "CborString.Value");
    }

    [Theory]
    [InlineData(false, "D81842FF")]
    [InlineData(false, "D9EA605F4100")]
    [InlineData(true, "D9EA606261")]
    public void X32DirectScalarCallersCatchTypedErrorsOnTruncatedTaggedContent(bool text, string hex)
    {
        byte[] data = Convert.FromHexString(hex);
        void Read()
        {
            if (text)
            {
                _ = CborString.Read(data);
            }
            else
            {
                _ = CborBytes.Read(data);
            }
        }

        CborDecodeException typed = Assert.Throws<CborDecodeException>(Read);
        Assert.Equal(text ? "Chrysalis.Codec.Types.CborString.Value" : "Chrysalis.Codec.Types.CborBytes.Value", typed.TypePath);
        FormatException format = Assert.ThrowsAny<FormatException>(Read);
        _ = Assert.IsType<CborDecodeException>(format);
    }

    [Theory]
    [InlineData(false, "42FF")]
    [InlineData(true, "6261")]
    public void X32DirectUntaggedScalarTruncationKeepsTheExistingException(bool text, string hex)
    {
        byte[] data = Convert.FromHexString(hex);
        _ = Assert.Throws<SAIB.Cbor.CborException>(() => text ? (ICborType)CborString.Read(data) : CborBytes.Read(data));
    }

    [Fact]
    public void X32TaggedEncodedGenericReaderAndRawSlice()
    {
        byte[] data = Convert.FromHexString("D8185F4182420102FF00");
        CborAny any = X32EagerAny.Read(data, out int consumed).Value;
        Assert.Equal(data.Length - 1, consumed);
        Assert.True(data.AsSpan(0, consumed).SequenceEqual(any.Raw.Span));
        CborEncodedValue wrapped = GenericSerializationUtil.ReadAnyWithConsumed<CborEncodedValue>(data, out consumed)!;
        Assert.Equal(data.Length - 1, consumed);
        Assert.Equal("820102", Convert.ToHexString(wrapped.Value.Span));
    }

    [Theory]
    [InlineData("820141FF", "item", 3, "integer", "byte string")]
    [InlineData("A141FFF5", "entry", 2, "integer", "byte string")]
    [InlineData("A119010041FF", "entry", 5, "boolean", "byte string")]
    public void X32ClosedCollectionErrorsNameTheItem(string hex, string slot, int offset, string expected, string actual)
    {
        byte[] wire = Convert.FromHexString(hex);
        if (slot == "item")
        {
            _ = AssertExactDecodeError(() => X32EagerIntList.Read(wire), offset, "Chrysalis.Codec.Test.X32EagerIntList.Value", expected, actual);
            _ = AssertExactDecodeError(() => _ = X32LazyIntList.Read(wire).Value, offset, "Chrysalis.Codec.Test.X32LazyIntList.Value.item", expected, actual);
        }
        else
        {
            _ = AssertExactDecodeError(() => X32EagerIntBoolMap.Read(wire), offset, "Chrysalis.Codec.Test.X32EagerIntBoolMap.Value", expected, actual);
            _ = AssertExactDecodeError(() => _ = X32LazyIntBoolMap.Read(wire).Value, offset, "Chrysalis.Codec.Test.X32LazyIntBoolMap.Value.entry", expected, actual);
        }
    }

    [Fact]
    public void X32ClosedCollectionErrorsStillNameTheHeaderType()
    {
        byte[] wire = [0x00];
        _ = AssertExactDecodeError(() => X32EagerIntList.Read(wire), 1, "Chrysalis.Codec.Test.X32EagerIntList.Value", "array", "unsigned integer");
        _ = AssertExactDecodeError(() => X32LazyIntList.Read(wire), 0, "Chrysalis.Codec.Test.X32LazyIntList.Value", "array", "unsigned integer");
        _ = AssertExactDecodeError(() => X32EagerIntBoolMap.Read(wire), 1, "Chrysalis.Codec.Test.X32EagerIntBoolMap.Value", "map", "unsigned integer");
        _ = AssertExactDecodeError(() => X32LazyIntBoolMap.Read(wire), 0, "Chrysalis.Codec.Test.X32LazyIntBoolMap.Value", "map", "unsigned integer");
    }

    private static CborDecodeException AssertExactDecodeError(Action decode, int offset, string path, string expected, string actual)
    {
        CborDecodeException error = AssertDecodeError(decode, offset, path);
        Assert.Equal(path, error.TypePath);
        Assert.Equal(expected, error.Expected);
        Assert.Equal(actual, error.Actual);
        Assert.Equal($"CBOR {path} at byte {offset}: expected {expected}; actual {actual}.", error.Message);
        return error;
    }

    private static void AssertRuntimeDecodeErrors(byte[] data, Type type, int offset, string path)
    {
        _ = AssertDecodeError(() => GenericSerializationUtil.Read(data, type), offset, path);
        _ = AssertDecodeError(() => GenericSerializationUtil.ReadAnyWithConsumed(data, type, out _), offset, path);
    }

    private static CborDecodeException AssertDecodeError(Action action, int offset, string path)
    {
        CborDecodeException error = Assert.ThrowsAny<CborDecodeException>(action);
        Assert.Equal(offset, error.Offset);
        Assert.Contains(path, error.TypePath, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(error.Expected));
        Assert.False(string.IsNullOrEmpty(error.Actual));
        return error;
    }

    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Fact]
    public void TransactionDeserializeAndSerialize()
    {
        string unsignedTx = "84A300D90102828258200177512CA37FED793D93474F0F8F1898E2B747ADE8D7C5F7572C64B478C5ACD00182582099DA5D9C5F595FC49AE2568E38919596940EE3C394421C27C643EE7AAA69F390000182A300581D70FA74D35F4B0D48AC6288AC5CEA71F28E807565AD09845232F229660F011A05F5E100028201D8185854D8799FD8799FD8799FD8799F581C5C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2FFFFD8799F40401A05F5E100FFD8799F40401A05F5E100FFD8799F40401A004C4B40FF00D8799FFFFFFF825839005C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2E8904EDC699E2F0CE7B72BE7CEC991DF651A222E2AE9244EB5975CBA1B0000000137EF0544021A00030AA9A0F5F6";

        PostMaryTransaction tx = CborSerializer.Deserialize<PostMaryTransaction>(Convert.FromHexString(unsignedTx));

        Assert.NotNull(tx.Body);
        Assert.NotNull(tx.Witnesses);

        string serializedHex = Convert.ToHexString(CborSerializer.Serialize(tx));
        Assert.Equal(unsignedTx, serializedHex, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenAllRequiredFieldsPresent()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = PersonRequired.Create(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed - all required fields are present
        PersonRequired deserialized = CborSerializer.Deserialize<PersonRequired>(cbor);
        Assert.Equal(1, deserialized.Id);
        Assert.Equal("John", deserialized.Name);
        Assert.Equal(25, deserialized.Age);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenDeserializingRequiredAsOptional()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = PersonRequired.Create(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed when deserializing as optional - all fields present
        PersonOptional optionalPerson = CborSerializer.Deserialize<PersonOptional>(cbor);
        Assert.Equal(1, optionalPerson.Id);
        Assert.Equal("John", optionalPerson.Name);
        Assert.Equal(25, optionalPerson.Age);
    }

    [Theory]
    // Babbage
    [InlineData("babbage.block")]
    public async Task DeserializeBabbageBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;
        const int iterationsPerTask = 1;

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
            {
                for (int i = 0; i < iterationsPerTask; i++)
                {
                    BabbageBlock block = CborSerializer.Deserialize<BabbageBlock>(cborRaw);
                    byte[] serialized = CborSerializer.Serialize(block);
                    _ = Convert.ToHexString(serialized);
                }
            }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    // Alonzo
    [InlineData("alonzo.block")]
    public async Task DeserializeAlonzoBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                AlonzoCompatibleBlock block = CborSerializer.Deserialize<AlonzoCompatibleBlock>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                _ = Convert.ToHexString(serialized);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    // Conway
    [InlineData("conway.block")]
    public async Task DeserializeConwayBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 2; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                ConwayBlock block = CborSerializer.Deserialize<ConwayBlock>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                _ = Convert.ToHexString(serialized);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("babbage-failing.block")]
    [InlineData("babbage-notsupported.block")]
    public void DeserializeFailingBabbageBlock(string filename)
    {
        byte[] cborRaw = LoadTestBlock(filename);
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Assert.Equal(6, block.EraNumber);
    }

    [Fact]
    public void DeserializeAlonzoWithDuplicateKey()
    {
        JsonElement golden = X7Tests.ReadPallasGolden("metadata");
        byte[] raw = LoadTestBlock(golden.GetProperty("source").GetString()!);
        X7Tests.AssertPallasInput(golden, raw);
        AlonzoCompatibleBlock block = AlonzoCompatibleBlock.Read(raw);
        Metadata labels = block.AuxiliaryDataSet.Value[golden.GetProperty("transactionIndex").GetInt32()] switch
        {
            Metadata metadata => metadata,
            ShellyMaAuxiliaryData metadata => metadata.TransactionMetadata,
            PostAlonzoAuxiliaryDataMap metadata => metadata.TransactionMetadata!.Value,
            _ => throw new InvalidOperationException("Expected transaction metadata")
        };
        MetadatumMap label = Assert.IsType<MetadatumMap>(labels.Value[golden.GetProperty("label").GetUInt64()]);
        MetadatumMap policy = Assert.Single(label.Value.Select(pair => pair.Value).OfType<MetadatumMap>());
        KeyValuePair<ITransactionMetadatum, ITransactionMetadatum>[] repetitions = [.. policy.Value.Where(pair =>
            pair.Key is MetadataText key && key.Value == "LoveForCrypto1of25")];
        Assert.Equal(X7Tests.PallasPairs(golden), repetitions.Select(pair =>
            (Convert.ToHexStringLower(pair.Key.Raw.Span), Convert.ToHexStringLower(pair.Value.Raw.Span))));
        Assert.Equal(policy.Raw.ToArray(), MetadatumMap.Create(policy.Value).Raw.ToArray());
        Assert.Equal(raw, AlonzoCompatibleBlock.Create(block.Header, block.TransactionBodies, block.TransactionWitnessSets,
            block.AuxiliaryDataSet, block.InvalidTransactions).Raw.ToArray());
    }

    [Fact]
    public void Create_TransactionInput_RoundTrip()
    {
        byte[] txIdBytes =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
            0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
        ];
        ReadOnlyMemory<byte> txId = txIdBytes;
        ulong index = 42;

        TransactionInput input = TransactionInput.Create(txId, index);

        Assert.True(input.TransactionId.Span.SequenceEqual(txId.Span));
        Assert.Equal(index, input.Index);

        // Verify serialization round-trip
        byte[] serialized = CborSerializer.Serialize(input);
        TransactionInput deserialized = CborSerializer.Deserialize<TransactionInput>(serialized);
        Assert.True(deserialized.TransactionId.Span.SequenceEqual(txId.Span));
        Assert.Equal(index, deserialized.Index);
    }

    [Fact]
    public void Create_ExUnits_RoundTrip()
    {
        ulong mem = 1_000_000;
        ulong steps = 2_000_000;

        ExUnits exUnits = ExUnits.Create(mem, steps);

        Assert.Equal(mem, exUnits.Mem);
        Assert.Equal(steps, exUnits.Steps);

        byte[] serialized = CborSerializer.Serialize(exUnits);
        ExUnits deserialized = CborSerializer.Deserialize<ExUnits>(serialized);
        Assert.Equal(mem, deserialized.Mem);
        Assert.Equal(steps, deserialized.Steps);
    }

    [Fact]
    public void Create_Lovelace_RoundTrip()
    {
        ulong amount = 5_000_000;

        Lovelace lovelace = Lovelace.Create(amount);

        Assert.Equal(amount, lovelace.Amount);

        byte[] serialized = CborSerializer.Serialize(lovelace);
        Lovelace deserialized = CborSerializer.Deserialize<Lovelace>(serialized);
        Assert.Equal(amount, deserialized.Amount);
    }

    [Fact]
    public void Create_PersonRequired_RoundTrip()
    {
        PersonRequired person = PersonRequired.Create(99, "Alice", 30);

        Assert.Equal(99, person.Id);
        Assert.Equal("Alice", person.Name);
        Assert.Equal(30, person.Age);

        byte[] serialized = CborSerializer.Serialize(person);
        PersonRequired deserialized = CborSerializer.Deserialize<PersonRequired>(serialized);
        Assert.Equal(99, deserialized.Id);
        Assert.Equal("Alice", deserialized.Name);
        Assert.Equal(30, deserialized.Age);
    }

    [Fact]
    public void Create_PersonOptional_WithNulls_RoundTrip()
    {
        PersonOptional person = PersonOptional.Create(1, null, null);

        Assert.Equal(1, person.Id);
        Assert.Null(person.Name);
        Assert.Null(person.Age);

        byte[] serialized = CborSerializer.Serialize(person);
        PersonOptional deserialized = CborSerializer.Deserialize<PersonOptional>(serialized);
        Assert.Equal(1, deserialized.Id);
        Assert.Null(deserialized.Name);
        Assert.Null(deserialized.Age);
    }

    [Fact]
    public void PlutusVoid_Serializes_To_D87980()
    {
        PlutusVoid v = new();
        byte[] serialized = CborSerializer.Serialize(v);
        Assert.Equal("D87980", Convert.ToHexString(serialized));
    }

    [Fact]
    public void PlutusVoid_Deserializes_From_D87980()
    {
        byte[] cbor = Convert.FromHexString("D87980");
        PlutusVoid v = CborSerializer.Deserialize<PlutusVoid>(cbor);
        _ = Assert.IsType<PlutusVoid>(v);
    }

    [Fact]
    public void PlutusVoid_RoundTrip()
    {
        PlutusVoid v = new();
        byte[] serialized = CborSerializer.Serialize(v);
        PlutusVoid deserialized = CborSerializer.Deserialize<PlutusVoid>(serialized);
        byte[] reserialized = CborSerializer.Serialize(deserialized);
        Assert.Equal(serialized, reserialized);
    }
}
