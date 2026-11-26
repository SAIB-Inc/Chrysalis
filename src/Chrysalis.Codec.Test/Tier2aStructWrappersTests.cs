using System.Buffers;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using ExplicitNullableAddress = Chrysalis.Codec.Types.Cardano.Core.Common.Address?;

namespace Chrysalis.Codec.Test;

public class Tier2aStructWrappersTests
{
    [Fact]
    public void AddressIsValueType() => Assert.True(typeof(Address).IsValueType);

    [Fact]
    public void EncodedValueIsValueType() => Assert.True(typeof(CborEncodedValue).IsValueType);

    [Fact]
    public void PostAlonzoAddressAccessAllocatesNothing()
    {
        PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Read(Convert.FromHexString("A20041AA0105"));
        AssertUnallocated(() => _ = output.Address);
    }

    [Fact]
    public void InlineDatumAccessAllocatesNothing()
    {
        InlineDatumOption datum = InlineDatumOption.Read(Convert.FromHexString("8201D8184101"));
        AssertUnallocated(() => _ = datum.Data);
    }

    [Fact]
    public void FreshEncodedValueWritesTag24()
    {
        ArrayBufferWriter<byte> writer = new();
        CborEncodedValue.Write(writer, new CborEncodedValue(new byte[] { 0x01 }));
        Assert.Equal("D8184101", Convert.ToHexString(writer.WrittenSpan));
    }

    [Fact]
    public void CostModelsAcceptExplicitNull() => Assert.Null(CostMdls.Read(Convert.FromHexString("A100F6")).PlutusV1);

    [Theory]
    [InlineData("A200F6018105", 5L)]
    [InlineData("A200F7018107", 7L)]
    public void CostModelsReadTheNextDefiniteEntryAfterNull(string hex, long coefficient)
    {
        CostMdls costMdls = CostMdls.Read(Convert.FromHexString(hex));
        Assert.Null(costMdls.PlutusV1);
        Assert.Equal(coefficient, Assert.Single(costMdls.PlutusV2!.Value));
    }

    [Fact]
    public void CostModelsPreserveIndefiniteBreakAfterUndefined()
    {
        const string hex = "BF00F7FF";
        CostMdls costMdls = CostMdls.Read(Convert.FromHexString(hex), out int consumed);
        Assert.Null(costMdls.PlutusV1);
        Assert.Equal(4, consumed);
        Assert.Equal(hex, Convert.ToHexString(costMdls.Raw.Span));
        Assert.Equal(hex, Convert.ToHexString(CborSerializer.Serialize(costMdls)));
    }

    [Fact]
    public void CostModelsReadTheNextIndefiniteEntryAfterUndefined()
    {
        const string hex = "BF00F7018107FF";
        CostMdls costMdls = CostMdls.Read(Convert.FromHexString(hex), out int consumed);
        Assert.Null(costMdls.PlutusV1);
        Assert.Equal(7L, Assert.Single(costMdls.PlutusV2!.Value));
        Assert.Equal(7, consumed);
        Assert.Equal(hex, Convert.ToHexString(costMdls.Raw.Span));
    }

    private static void AssertUnallocated(Action read) => Assert.Equal(0, AllocationAssert.Measure(() =>
    {
        for (int i = 0; i < 64; i++)
        {
            read();
        }
    }, read));

    [Fact]
    public void WrapperSizeIsTwoMemoryHandles()
    {
        Assert.Equal(32, Unsafe.SizeOf<Address>());
        Assert.Equal(32, Unsafe.SizeOf<CborEncodedValue>());
    }

    [Fact]
    public void OtherFieldsAndDefiniteReadsAllocateNothing()
    {
        byte[] address = Convert.FromHexString("41AA");
        byte[] encoded = Convert.FromHexString("D8184101");
        AlonzoTransactionOutput alonzo = AlonzoTransactionOutput.Read(Convert.FromHexString("8241AA05"));
        PostAlonzoTransactionOutput postAlonzo = PostAlonzoTransactionOutput.Read(Convert.FromHexString("A30041AA010503D8184101"));
        AssertUnallocated(() => _ = alonzo.Address);
        AssertUnallocated(() => _ = postAlonzo.ScriptRef);
        AssertUnallocated(() => _ = Address.Read(address));
        AssertUnallocated(() => _ = CborEncodedValue.Read(encoded));
        Assert.Equal(1, postAlonzo.ScriptRef!.Value.Deserialize<PlutusInt>().Value);
    }

    [Theory]
    [InlineData("", "40", "D81840")]
    [InlineData("01", "4101", "D8184101")]
    public void FreshWrappersAreConsistentAcrossApis(string payloadHex, string addressHex, string encodedHex)
    {
        ArgumentNullException.ThrowIfNull(addressHex);
        ArgumentNullException.ThrowIfNull(encodedHex);
        byte[] payload = Convert.FromHexString(payloadHex);
        Address address = payload.Length == 0 ? default : new(payload);
        CborEncodedValue encoded = payload.Length == 0 ? default : new(payload);
        CheckFresh(address, addressHex, Address.Write);
        CheckFresh(encoded, encodedHex, CborEncodedValue.Write);
        Address ownedAddress = Address.Create(payload);
        CborEncodedValue ownedEncoded = CborEncodedValue.Create(payload);
        Assert.Equal(addressHex, Convert.ToHexString(ownedAddress.Raw.Span));
        Assert.Equal(encodedHex, Convert.ToHexString(ownedEncoded.Raw.Span));
        Assert.Equal($"A100{encodedHex}", Convert.ToHexString(CborSerializer.Serialize(CborMap<int, CborEncodedValue>.Create([new(0, encoded)]))));
        Assert.Equal($"81{encodedHex}", Convert.ToHexString(CborSerializer.Serialize(CborList.Create([encoded]))));
        if (payload.Length != 0)
        {
            payload[0] = 0xAA;
            Assert.Equal(0xAA, address.Value.Span[0]);
            Assert.Equal(0xAA, encoded.Value.Span[0]);
            Assert.Equal(payloadHex, Convert.ToHexString(ownedAddress.Value.Span));
            Assert.Equal(payloadHex, Convert.ToHexString(ownedEncoded.Value.Span));
        }
    }

    private static void CheckFresh<T>(T value, string hex, WriteHandler<T> write) where T : ICborType
    {
        Assert.True(value.Raw.IsEmpty);
        ArrayBufferWriter<byte> writer = new();
        write(writer, value);
        Assert.Equal(hex, Convert.ToHexString(writer.WrittenSpan));
        Assert.Equal(hex, Convert.ToHexString(CborSerializer.Serialize(value)));
        Assert.Equal(hex.Length / 2, CborSerializer.GetEncodedLength(value));
    }

    [Theory]
    [InlineData(null, "A20041AA0105")]
    [InlineData("", "A30041AA010503D81840")]
    [InlineData("01", "A30041AA010503D8184101")]
    public void LazyOutputCreateAndReadDistinguishNullFromEmpty(string? payload, string hex)
    {
        CborEncodedValue? script = payload switch { null => null, "" => default(CborEncodedValue), _ => new CborEncodedValue(Convert.FromHexString(payload)) };
        PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(new Address(Convert.FromHexString("AA")), Lovelace.Create(5), null, script);
        Assert.Equal(hex, Convert.ToHexString(output.Raw.Span));
        Assert.Equal(hex, Convert.ToHexString(CborSerializer.Serialize(output)));
        Assert.Equal(payload, PayloadHex(PostAlonzoTransactionOutput.Read(Convert.FromHexString(hex)).ScriptRef?.Value));
        Assert.Null(PostAlonzoTransactionOutput.Read(Convert.FromHexString("A30041AA010503F6")).ScriptRef);
    }

    [Theory]
    [InlineData(null, "F6", "F6")]
    [InlineData("", "40", "D81840")]
    [InlineData("01", "4101", "D8184101")]
    public void FreshEagerMapsAndListsHonorNullableAnnotations(string? payload, string addressHex, string encodedHex)
    {
        byte[] bytes = Convert.FromHexString(payload ?? "");
        Address? address = payload switch { null => null, "" => default(Address), _ => new Address(bytes) };
        CborEncodedValue? encoded = payload switch { null => null, "" => default(CborEncodedValue), _ => new CborEncodedValue(bytes) };
        CheckField(new Tier2OptionalAddressMap(address), Tier2OptionalAddressMap.Write, Tier2OptionalAddressMap.Read, x => x.Item?.Value, payload, payload is null ? "A0" : $"A100{addressHex}", "A100F6");
        CheckField(new Tier2NullableAddressMap(address), Tier2NullableAddressMap.Write, Tier2NullableAddressMap.Read, x => x.Item?.Value, payload, $"A100{addressHex}", "A0");
        CheckField(new Tier2OptionalAddressList(address), Tier2OptionalAddressList.Write, Tier2OptionalAddressList.Read, x => x.Item?.Value, payload, payload is null ? "80" : $"81{addressHex}", "81F6");
        CheckField(new Tier2NullableAddressList(address), Tier2NullableAddressList.Write, Tier2NullableAddressList.Read, x => x.Item?.Value, payload, $"81{addressHex}", "81F6");
        CheckField(new Tier2OptionalEncodedMap(encoded), Tier2OptionalEncodedMap.Write, Tier2OptionalEncodedMap.Read, x => x.Item?.Value, payload, payload is null ? "A0" : $"A100{encodedHex}", "A100F6");
        CheckField(new Tier2NullableEncodedMap(encoded), Tier2NullableEncodedMap.Write, Tier2NullableEncodedMap.Read, x => x.Item?.Value, payload, $"A100{encodedHex}", "A0");
        CheckField(new Tier2OptionalEncodedList(encoded), Tier2OptionalEncodedList.Write, Tier2OptionalEncodedList.Read, x => x.Item?.Value, payload, payload is null ? "80" : $"81{encodedHex}", "81F6");
        CheckField(new Tier2NullableEncodedList(encoded), Tier2NullableEncodedList.Write, Tier2NullableEncodedList.Read, x => x.Item?.Value, payload, $"81{encodedHex}", "81F6");
    }

    private static string? PayloadHex(ReadOnlyMemory<byte>? value) => value is { } bytes ? Convert.ToHexString(bytes.Span) : null;

    private static void CheckField<T>(T fresh, WriteHandler<T> write, ReadHandler<T> read, Func<T, ReadOnlyMemory<byte>?> value, string? payload, string wire, string nullWire) where T : ICborType
    {
        CheckFresh(fresh, wire, write);
        Assert.Equal(payload, PayloadHex(value(read(Convert.FromHexString(wire)))));
        Assert.Null(value(read(Convert.FromHexString(nullWire))));
    }

    [Fact]
    public void MapReadersDistinguishOuterFromInnerNullable()
    {
        byte[] explicitNull = Convert.FromHexString("A100F6");
        Assert.Null(CostMdls.Read(Convert.FromHexString("A0")).PlutusV1);
        Assert.Null(CostMdls.Read(Convert.FromHexString("A100F7")).PlutusV1);
        Assert.Null(Tier2ExplicitNullableAddressMap.Read(explicitNull).Item);
        Assert.Equal("AA", PayloadHex(Tier2ExplicitNullableAddressMap.Read(Convert.FromHexString("A10041AA")).Item?.Value));
        _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<Tier2BoxMap>(explicitNull));
        Assert.Equal("a", Tier2BoxMap.Read(Convert.FromHexString("A100816161")).Item.Value);
    }

    [Fact]
    public void ExplicitNullableAddressMapCreateHandlesNull()
    {
        Tier2ExplicitNullableAddressMap created = Tier2ExplicitNullableAddressMap.Create(null);
        Assert.Equal("A0", Convert.ToHexString(created.Raw.Span));
        Assert.Null(created.Item);
        Assert.Equal("A0", Convert.ToHexString(CborSerializer.Serialize(created)));
    }

    [Fact]
    public void ExplicitNullableAddressMapWriteHandlesNull() =>
        CheckFresh(new Tier2ExplicitNullableAddressMap(null), "A0", Tier2ExplicitNullableAddressMap.Write);

    [Fact]
    public void ExplicitNullableAddressMapCreateAndWriteHandleDefaultAddress()
    {
        Tier2ExplicitNullableAddressMap created = Tier2ExplicitNullableAddressMap.Create(default(Address));
        Assert.Equal("A10040", Convert.ToHexString(created.Raw.Span));
        Address item = Assert.IsType<Address>(created.Item);
        Assert.True(item.Value.IsEmpty);
        Assert.Equal("A10040", Convert.ToHexString(CborSerializer.Serialize(created)));

        CheckFresh(new Tier2ExplicitNullableAddressMap(default(Address)), "A10040", Tier2ExplicitNullableAddressMap.Write);
    }

    [Fact]
    public void ExplicitNullableAddressMapCreateAndWriteHandleNonNullAddress()
    {
        Address address = new(new byte[] { 0xAA });
        Tier2ExplicitNullableAddressMap created = Tier2ExplicitNullableAddressMap.Create(address);
        Assert.Equal("A10041AA", Convert.ToHexString(created.Raw.Span));
        Assert.Equal("AA", PayloadHex(created.Item?.Value));
        Assert.Equal("A10041AA", Convert.ToHexString(CborSerializer.Serialize(created)));

        CheckFresh(new Tier2ExplicitNullableAddressMap(address), "A10041AA", Tier2ExplicitNullableAddressMap.Write);
        Assert.Equal("AA", PayloadHex(Tier2ExplicitNullableAddressMap.Read(Convert.FromHexString("A10041AA")).Item?.Value));
    }

    [Fact]
    public void BoxMapCreateAndWriteHandleInnerNullableWhileRejectingOuterNull()
    {
        const string okHex = "A10081626F6B";
        Tier2BoxMap created = Tier2BoxMap.Create(new Tier2Box<string?>("ok"));
        Assert.Equal(okHex, Convert.ToHexString(created.Raw.Span));
        Assert.Equal("ok", created.Item.Value);
        Assert.Equal(okHex, Convert.ToHexString(CborSerializer.Serialize(created)));

        CheckFresh(new Tier2BoxMap(new Tier2Box<string?>("ok")), okHex, Tier2BoxMap.Write);
        Assert.Equal("ok", Tier2BoxMap.Read(Convert.FromHexString(okHex)).Item.Value);
        _ = Assert.Throws<SAIB.Cbor.CborException>(() => Tier2BoxMap.Read(Convert.FromHexString("A100F6")));
        _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<Tier2BoxMap>(Convert.FromHexString("A100F6")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FacadeReadsNullableAndNonNullableInBothOrders(bool nullableFirst)
    {
        CheckFacade<Address>("41AA", nullableFirst);
        CheckFacade<CborEncodedValue>("D8184101", nullableFirst);
    }

    private static void CheckFacade<T>(string hex, bool nullableFirst) where T : struct, ICborType
    {
        byte[] padded = Convert.FromHexString(hex + "00");
        if (nullableFirst) { Check<T?>(); Check<T>(); }
        else { Check<T>(); Check<T?>(); }

        void Check<TRead>()
        {
            T first = Assert.IsType<T>(CborSerializer.Deserialize<TRead>(padded));
            T second = Assert.IsType<T>(CborSerializer.Deserialize<TRead>(padded, out int consumed));
            Assert.Equal(hex, Convert.ToHexString(first.Raw.Span));
            Assert.Equal(first, second);
            Assert.Equal(hex.Length / 2, consumed);
            byte[] nullWire = [0xF6];
            _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<TRead>(nullWire));
            _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<TRead>(nullWire, out _));
        }
    }

    [Fact]
    public void EqualityUsesValueAndRawHandles()
    {
        byte[] left = Convert.FromHexString("41AA");
        byte[] right = Convert.FromHexString("41AA");
        CheckHandles(left, right, Address.Read, value => new Address(value));
        CheckHandles(left, right, CborEncodedValue.Read, value => new CborEncodedValue(value));
        Assert.True(default == new Address(default));
        Assert.True(default == new CborEncodedValue(default));
    }

    private static void CheckHandles<T>(byte[] wire, byte[] other, ReadHandler<T> read, Func<ReadOnlyMemory<byte>, T> wrap) where T : struct
    {
        T first = read(wire);
        T same = read(wire);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, read(other));
        Assert.NotEqual(first, wrap(wire.AsMemory(1)));
        Assert.NotEqual(wrap(wire.AsMemory(1)), wrap(other.AsMemory(1)));
    }

    [Fact]
    public void DefiniteReadsBorrowAtAnOffsetAndStopBeforeTrailingItem()
    {
        byte[] bytes = Convert.FromHexString("FFFFD81841AA00");
        Address address = Address.Read(bytes.AsMemory(4), out int addressConsumed);
        CborEncodedValue encoded = CborEncodedValue.Read(bytes.AsMemory(2), out int encodedConsumed);
        Assert.Equal(2, addressConsumed);
        Assert.Equal(4, encodedConsumed);
        bytes[5] = 0xBB;
        Assert.Equal("BB", PayloadHex(address.Value));
        Assert.Equal("BB", PayloadHex(encoded.Value));
        Assert.Equal("41BB", Convert.ToHexString(address.Raw.Span));
        Assert.Equal("D81841BB", Convert.ToHexString(encoded.Raw.Span));
    }

    [Fact]
    public void DirectWritersPreserveIndefiniteAndBareRaw()
    {
        byte[] chunks = Convert.FromHexString("5F41AA41BBFF");
        Address address = Address.Read(chunks);
        byte[] taggedChunks = Convert.FromHexString("D8185F41AA41BBFF");
        CborEncodedValue tagged = CborEncodedValue.Read(taggedChunks);
        CborEncodedValue bare = CborEncodedValue.Read(Convert.FromHexString("4101"));
        ArrayBufferWriter<byte> writer = new();
        Address.Write(writer, address);
        CborEncodedValue.Write(writer, bare);
        Assert.Equal("5F41AA41BBFF4101", Convert.ToHexString(writer.WrittenSpan));
        Assert.Equal("4101", Convert.ToHexString(CborSerializer.Serialize(bare)));
        chunks[2] = taggedChunks[4] = 0xCC;
        Assert.Equal("AABB", PayloadHex(address.Value));
        Assert.Equal("AABB", PayloadHex(tagged.Value));
    }

    [Fact]
    public void DirectWritersRejectNullOutput()
    {
        _ = Assert.Throws<ArgumentNullException>("output", () => Address.Write(null!, default));
        _ = Assert.Throws<ArgumentNullException>("output", () => CborEncodedValue.Write(null!, default));
    }

    [Fact]
    public void MapsRejectWireNullWrapperKeysAndValues()
    {
        Check<Address>();
        Check<CborEncodedValue>();

        static void Check<T>() where T : struct, ICborType
        {
            byte[] nullKey = Convert.FromHexString("A1F601");
            CborDecodeException keyError = Assert.Throws<CborDecodeException>(() => CborMap<T, int>.Read(nullKey));
            Assert.Equal((1, "CborMap.entry", "null"), (keyError.Offset, keyError.TypePath, keyError.Actual));

            byte[] nullValue = Convert.FromHexString("A101F6");
            CborMap<int, T> map = CborMap<int, T>.Read(nullValue);
            Assert.True(map.ContainsKey(1));
            CborDecodeException valueError = Assert.Throws<CborDecodeException>(() => _ = map[1]);
            Assert.Equal((2, "CborMap.entry", "null"), (valueError.Offset, valueError.TypePath, valueError.Actual));
        }
    }

    [Theory]
    [InlineData("", 0, "end of input")]
    [InlineData("D8", 1, "end of input")]
    [InlineData("D818D81841AA", 3, "semantic tag")]
    [InlineData("6161", 1, "text string")]
    public void DirectReaderErrorsPreserveTypePathAndOffset(string wire, int offset, string actual)
    {
        ReadOnlyMemory<byte> bytes = Convert.FromHexString("FF" + wire).AsMemory(1);
        Check(Assert.Throws<CborDecodeException>(() => Address.Read(bytes)), typeof(Address).FullName!);
        Check(Assert.Throws<CborDecodeException>(() => CborEncodedValue.Read(bytes)), typeof(CborEncodedValue).FullName!);

        void Check(CborDecodeException error, string path)
        {
            Assert.Equal(path + ".Value", error.TypePath);
            Assert.Equal(offset, error.Offset);
            Assert.Equal("byte string", error.Expected);
            Assert.Equal(actual, error.Actual);
        }
    }

    [Fact]
    public void NetworkEnvelopesRoundTripFreshEncodedValues()
    {
        CborEncodedValue body = new(new byte[] { 0x80 });
        CheckFresh(new BlockBody(4, body), "8204D8184180", BlockBody.Write);
        Assert.Equal("80", PayloadHex(BlockBody.Read(Convert.FromHexString("8204D8184180")).Body.Value));
        N2CMessageRollForward forward = new(2, body, new Tip(new OriginPoint(), 0));
        const string forwardHex = "8302D8184180828000";
        CheckFresh(forward, forwardHex, N2CMessageRollForward.Write);
        Assert.Equal("80", PayloadHex(N2CMessageRollForward.Read(Convert.FromHexString(forwardHex)).Payload.Value));
    }
}

[CborSerializable, CborMap]
public partial record Tier2OptionalAddressMap([CborProperty(0)] Address? Item) : CborRecord;
[CborSerializable, CborMap]
public partial record Tier2NullableAddressMap([CborProperty(0), CborNullable] Address? Item) : CborRecord;
[CborSerializable, CborList]
public partial record Tier2OptionalAddressList([CborOrder(0)] Address? Item) : CborRecord;
[CborSerializable, CborList]
public partial record Tier2NullableAddressList([CborOrder(0), CborNullable] Address? Item) : CborRecord;
[CborSerializable, CborMap]
public partial record Tier2OptionalEncodedMap([CborProperty(0)] CborEncodedValue? Item) : CborRecord;
[CborSerializable, CborMap]
public partial record Tier2NullableEncodedMap([CborProperty(0), CborNullable] CborEncodedValue? Item) : CborRecord;
[CborSerializable, CborList]
public partial record Tier2OptionalEncodedList([CborOrder(0)] CborEncodedValue? Item) : CborRecord;
[CborSerializable, CborList]
public partial record Tier2NullableEncodedList([CborOrder(0), CborNullable] CborEncodedValue? Item) : CborRecord;
[CborSerializable, CborMap]
public partial record Tier2ExplicitNullableAddressMap([CborProperty(0)] ExplicitNullableAddress Item) : CborRecord;
[CborSerializable, CborMap]
public partial record Tier2BoxMap([CborProperty(0)] Tier2Box<string?> Item) : CborRecord;
[CborSerializable, CborList]
public partial record Tier2Box<T>([CborOrder(0)] T Value) : CborRecord;
