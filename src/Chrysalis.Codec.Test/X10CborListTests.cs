using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using SAIB.Cbor.Serialization;
using Xunit.Abstractions;

namespace Chrysalis.Codec.Test;

public class X10CborListTests(ITestOutputHelper output)
{
    private static readonly int[] ExpectedItems = [2, 3, 2];

    [Fact]
    public void ListTypeIsAValueTypeAndGetValueWasRemoved()
    {
        Type list = typeof(CborList<int>);
        Assert.True(list.IsValueType);
        Assert.Null(list.GetMethod("GetValue", Type.EmptyTypes));
        Assert.Null(list.Assembly.GetType("Chrysalis.Codec.Extensions.CborMaybeIndefListExtensions"));
    }

    [Fact]
    public void PinnedOutsideListsCoverAllFourFormsAndRoundTrip()
    {
        string data = Path.Combine(AppContext.BaseDirectory, "TestData");
        HashSet<(bool Indefinite, bool Tagged)> seen = [];
        foreach (string line in File.ReadLines(Path.Combine(data, "x10-list-vectors.tsv")).Skip(1))
        {
            string[] columns = line.Split('\t');
            string path = Path.Combine(data, columns[0]);
            int offset = int.Parse(columns[1], CultureInfo.InvariantCulture);
            int size = int.Parse(columns[2], CultureInfo.InvariantCulture);
            int count = int.Parse(columns[3], CultureInfo.InvariantCulture);
            bool indefinite = bool.Parse(columns[4]);
            bool tagged = bool.Parse(columns[5]);
            byte[] fixture = Path.GetExtension(path) switch
            {
                ".tx" => ReadCliWire(File.ReadAllBytes(path)),
                ".block" => Convert.FromHexString(File.ReadAllText(path).Trim()),
                _ => File.ReadAllBytes(path)
            };
            ReadOnlyMemory<byte> suffix = fixture.AsMemory(offset);
            CborReader shape = new(suffix.Span);
            bool wireTagged = (shape.Buffer[0] >> 5) == 6;
            if (wireTagged)
            {
                Assert.True(shape.TryReadSemanticTag(out ulong tag));
                Assert.Equal(258UL, tag);
            }
            Assert.Equal(4, shape.Buffer[0] >> 5);
            shape.ReadBeginArray();
            bool wireIndefinite = shape.ReadSize() == -1;
            Assert.Equal((indefinite, tagged), (wireIndefinite, wireTagged));
            switch (columns[6])
            {
                case "unsigned": AssertListRoundTrip<ulong>(suffix, size, count, indefinite, tagged); break;
                case "byron-input": AssertListRoundTrip<ByronTxIn>(suffix, size, count, indefinite, tagged); break;
                case "transaction-input": AssertListRoundTrip<TransactionInput>(suffix, size, count, indefinite, tagged); break;
                default: throw new InvalidOperationException($"Unrecognized X10 fixture item type: {columns[6]}");
            }
            _ = seen.Add((wireIndefinite, wireTagged));
            output.WriteLine($"{columns[0]}+{offset}: {(indefinite ? "indefinite" : "definite")}, {(tagged ? "tag 258" : "plain")}");
        }
        Assert.True(seen.SetEquals([(false, false), (false, true), (true, false), (true, true)]), "The outside fixture manifest must cover all four wire forms.");
    }

    [Fact]
    public void CliGeneratedInputsMatchTheReplay()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "X10Tag258Indef.tx");
        byte[] envelope = File.ReadAllBytes(path);
        Assert.Equal("E319BA89F6CE078248E3B8AEA3F68D1F15AE3EA6F916F64814618489418F618F", Convert.ToHexString(SHA256.HashData(envelope)));
        byte[] wire = ReadCliWire(envelope);
        ConwayTransactionBody body = CborSerializer.Deserialize<ConwayTransactionBody>(wire.AsMemory(1));
        Assert.Equal(24, body.Inputs.Count);
        Assert.Equal(Enumerable.Range(0, 24).Select(index => (ulong)index), body.Inputs.Select(input => input.Index));
    }

    internal static byte[] ReadCliWire(byte[] envelope)
    {
        using JsonDocument tx = JsonDocument.Parse(envelope);
        return Convert.FromHexString(tx.RootElement.GetProperty("cborHex").GetString()!);
    }

    private static void AssertListRoundTrip<T>(ReadOnlyMemory<byte> suffix, int size, int count, bool indefinite, bool tagged)
    {
        byte[] expected = suffix[..size].ToArray();
        CborList<T> direct = CborList<T>.Read(suffix, out int directConsumed);
        CborList<T> generic = CborSerializer.Deserialize<CborList<T>>(suffix, out int genericConsumed);
        CborList<T> noCount = CborSerializer.Deserialize<CborList<T>>(suffix);

        Assert.Equal(size, directConsumed);
        Assert.Equal(size, genericConsumed);
        Assert.Equal(suffix[..size], direct.Raw); // same underlying memory and range
        Assert.Equal(direct, generic);
        Assert.Equal(direct, noCount);
        Assert.Equal(count, direct.Count);
        Assert.Equal(indefinite, direct.IsIndefinite);
        Assert.Equal(tagged, direct.HasSetTag);
        Assert.Equal(expected, CborSerializer.Serialize(direct));

        CborList<T> fresh = CborList.Create(direct, indefinite, tagged);
        Assert.True(fresh.Raw.IsEmpty);
        Assert.Equal(count, fresh.Count);
        ArrayBufferWriter<byte> output = new();
        CborList<T>.Write(output, fresh);
        Assert.Equal(expected, output.WrittenMemory.ToArray());
        Assert.Equal(expected, CborSerializer.Serialize(fresh));
    }

    [Theory]
    [InlineData(false, false, "83020302")]
    [InlineData(false, true, "D9010283020302")]
    [InlineData(true, false, "9F020302FF")]
    [InlineData(true, true, "D901029F020302FF")]
    public void BuildFromItemsWithoutRawPreservesOrderAndForm(bool indefinite, bool tagged, string hex)
    {
        int[] items = [2, 3, 2];
        CborList<int> list = CborList.Create(items, indefinite, tagged);
        items[0] = 1;

        Assert.True(list.Raw.IsEmpty);
        Assert.Equal(3, list.Count);
        Assert.Equal(indefinite, list.IsIndefinite);
        Assert.Equal(tagged, list.HasSetTag);
        CborListEnumerator<int> iterator = list.GetEnumerator();
        Assert.True(iterator.MoveNext());
        Assert.Equal(2, iterator.Current);
        Assert.Equal(ExpectedItems, list.ToArray());
        Assert.Equal(Convert.FromHexString(hex), CborSerializer.Serialize(list));
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("D81880")]
    [InlineData("D90102D9010280")]
    [InlineData("D901029F01")]
    [InlineData("8201")]
    [InlineData("81FF")]
    [InlineData("D9")]
    public void RejectsWrongTagShapeOrTruncation(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        CborDecodeException direct = Assert.Throws<CborDecodeException>(() => CborList<int>.Read(bytes));
        CborDecodeException consumed = Assert.Throws<CborDecodeException>(() => CborList<int>.Read(bytes, out _));
        CborDecodeException generic = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<CborList<int>>(bytes));
        Assert.Equal(direct.Offset, consumed.Offset);
        Assert.Equal(direct.Offset, generic.Offset);
    }

    [Fact]
    public void EqualityUsesStorageAndDefaultIsNotACreatedEmptyList()
    {
        CborList<int> decoded = CborList<int>.Read(new byte[] { 0x81, 1 });
        CborList<int> copy = decoded;
        Assert.True(decoded == copy);
        Assert.Equal(decoded.GetHashCode(), copy.GetHashCode());
        Assert.False(decoded == CborList<int>.Read(decoded.Raw.ToArray()));
        CborList<int> fresh = CborList.Create([1]);
        Assert.False(fresh == CborList.Create([1]));
        Assert.True(fresh.SequenceEqual(decoded));

        CborList<int> empty = CborList.Create<int>([]);
        CborList<int> missing = default;
        Assert.True(missing.Raw.IsEmpty);
        Assert.True(empty.Raw.IsEmpty);
        Assert.Equal(0, missing.Count);
        Assert.Equal(0, empty.Count);
        Assert.Empty(missing);
        Assert.Empty(empty);
        Assert.False(missing == empty);
        Assert.Equal(new byte[] { 0x80 }, CborSerializer.Serialize(empty));
        _ = Assert.Throws<CborValidationException>(() => CborList<int>.Write(new ArrayBufferWriter<byte>(), missing));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Serialize(missing));
        _ = Assert.Throws<ArgumentNullException>(() => CborList.Create<int>(null!));
    }

    [Fact]
    public void GeneratedPropertiesSupportFallbackReadersNestedAndNullableItems()
    {
        CborLabel label = new("language");
        CborList<int> inner = CborList.Create([2], isIndefinite: true, hasSetTag: true);
        SpecializedListProperty fresh = SpecializedListProperty.Create(CborList.Create([1.25m]), CborList.Create([label]),
            CborList.Create([inner]), CborList.Create<int?>([42]), CborList.Create<int?>([7]));
        byte[] bytes = CborSerializer.Serialize(fresh);
        SpecializedListProperty raw = SpecializedListProperty.Read(bytes);
        Assert.Equal(1.25m, Assert.Single(raw.Decimals));
        Assert.Equal(label, Assert.Single(raw.Labels));
        CborList<int> decodedInner = Assert.Single(raw.Nested);
        Assert.Equal(2, Assert.Single(decodedInner));
        Assert.True(decodedInner.IsIndefinite);
        Assert.True(decodedInner.HasSetTag);
        Assert.Equal(Convert.FromHexString("81D901029F02FF"), CborSerializer.Serialize(raw.Nested));
        Assert.Equal(42, Assert.Single(raw.NullableItems));
        Assert.Equal(7, Assert.Single(raw.Optional!.Value));
        Assert.Equal(bytes, CborSerializer.Serialize(raw));

        SpecializedListProperty invalid = SpecializedListProperty.Read(Convert.FromHexString("A40080018002800381F6"));
        _ = Assert.Throws<CborDecodeException>(() => invalid.NullableItems.First());
    }

    [Fact]
    public void OpenGenericListPropertyReadsAndWrites()
    {
        byte[] wire = Convert.FromHexString("A1009F182AFF");
        GenericListProperty<int> raw = GenericListProperty<int>.Read(wire);
        Assert.Equal(42, Assert.Single(raw.Items));
        Assert.True(raw.Items.IsIndefinite);
        Assert.Equal(wire, CborSerializer.Serialize(raw));
        GenericListProperty<int> fresh = GenericListProperty<int>.Create(CborList.Create([42], isIndefinite: true));
        Assert.Equal(wire, CborSerializer.Serialize(fresh));
    }

    [Fact]
    public void GeneratedLazyPropertyReadsAndWritesWithoutABoxedList()
    {
        byte[] bytes = Convert.FromHexString("A200D901029F182AFF0180");
        ListProperty record = CborSerializer.Deserialize<ListProperty>(bytes);
        CborList<int> ReadList() => record.Items;
        int observed = 0;
        void ReadProperty() => observed = ReadList().Count;

        long allocated = AllocationAssert.Measure(ReadProperty, ReadProperty);
        Assert.Equal(1, observed);
        Assert.Equal(0, allocated);
        Assert.Equal(42, Assert.Single(record.Items));
        Assert.Equal(0, record.Optional!.Value.Count);
        Assert.Null(CborSerializer.Deserialize<ListProperty>(Convert.FromHexString("A10080")).Optional);

        ListProperty fresh = ListProperty.Create(CborList.Create([42], true, true), CborList.Create<int>([]));
        Assert.Equal(bytes, fresh.Raw.ToArray());
        ArrayBufferWriter<byte> writer = new();
        CborList<int>.Write(writer, record.Items);
        Assert.Equal(record.Items.Raw.ToArray(), writer.WrittenMemory.ToArray());
    }
}

[CborSerializable, CborMap]
public readonly partial record struct ListProperty : ICborType
{
    [CborProperty(0)] public partial CborList<int> Items { get; }
    [CborProperty(1)] public partial CborList<int>? Optional { get; }
}

[CborSerializable, CborMap]
public readonly partial record struct SpecializedListProperty : ICborType
{
    [CborProperty(0)] public partial CborList<decimal> Decimals { get; }
    [CborProperty(1)] public partial CborList<CborLabel> Labels { get; }
    [CborProperty(2)] public partial CborList<CborList<int>> Nested { get; }
    [CborProperty(3)] public partial CborList<int?> NullableItems { get; }
    [CborProperty(4)] public partial CborList<int?>? Optional { get; }
}

[CborSerializable, CborMap]
public readonly partial record struct GenericListProperty<T> : ICborType
{
    [CborProperty(0)] public partial CborList<T> Items { get; }
}
