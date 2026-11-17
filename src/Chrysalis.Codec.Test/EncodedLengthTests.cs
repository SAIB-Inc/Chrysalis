using System.Buffers;
using System.Runtime.Serialization;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using SAIB.Cbor.Attributes;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

[CborSerializable]
public partial record LengthFields(string Value) : CborRecord;

[CborSerializable]
public partial record LengthBox<T>(T Value) : CborRecord;

[CborSerializable]
[CborList]
public partial record LengthEnvelope(
    [CborOrder(0)] CborEncodedValue Encoded,
    [CborOrder(1)] CborAny Any) : CborRecord;

public class EncodedLengthTests
{
    [Fact]
    public void PreservedBytes_UseTheirLengthWithoutReencoding()
    {
        LengthFields value = new("not the preserved value") { Raw = new byte[] { 0x78, 0x01, 0x61 } };
        Assert.Equal(value.Raw.Length, CborSerializer.GetEncodedLength(value));
        Assert.Equal(CborSerializer.Serialize(value).Length, CborSerializer.GetEncodedLength(value));
    }

    [Fact]
    public void CborAnyAndTag24_WithoutPreservedContainerBytes_ReachTheSink()
    {
        LengthEnvelope value = new(new(new byte[] { 0x18, 0x2a }), new(new byte[] { 0x9f, 0x01, 0xff }));
        Assert.True(value.Raw.IsEmpty);
        byte[] bytes = CborSerializer.Serialize(value);
        Assert.Equal("82D81842182A9F01FF", Convert.ToHexString(bytes));
        Assert.Equal(bytes.Length, CborSerializer.GetEncodedLength(value));
    }

    [Fact]
    public void NestedGeneric_MatchesSerializedLength()
    {
        LengthBox<LengthBox<LengthFields>> value = new(new(new("nested fields")));
        Assert.True(value.Raw.IsEmpty);
        Assert.Equal(CborSerializer.Serialize(value).Length, CborSerializer.GetEncodedLength(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(8192)]
    public void RecordWithoutPreservedBytes_MatchesSerializedLength(int length)
    {
        LengthFields value = new(new string('\u20ac', length));
        Assert.True(value.Raw.IsEmpty);
        long expected = CborSerializer.Serialize(value).Length;
        Assert.Equal(expected, CborSerializer.GetEncodedLength(value));
        Assert.Equal(expected, CborSerializer.GetEncodedLength(value));
    }

    [Fact]
    [Trait("Category", "Allocation")]
    public void PreservedBytes_AllocateNothingAfterOneWarmQuery() =>
        AssertAllocationFree(new LengthFields("unused") { Raw = "aa"u8.ToArray() });

    [Fact]
    [Trait("Category", "Allocation")]
    public void RecordWithoutPreservedBytes_AllocatesNothingAfterOneWarmQuery() =>
        AssertAllocationFree(new LengthFields(new string('\u20ac', 8192)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Callbacks_RunOncePerWritingPassUnlessBytesArePreserved(bool preserved)
    {
        CallbackValue value = new(new CallbackPayload());
        if (preserved)
        {
            value.Raw = new byte[] { 0xa0 };
        }

        _ = CborSerializer.GetEncodedLength(value);
        Assert.Equal(preserved ? 0 : 1, value.Payload.Calls);
        _ = CborSerializer.Serialize(value);
        Assert.Equal(preserved ? 0 : 2, value.Payload.Calls);
    }

    [Fact]
    public void HandWrittenWrite_NestedLengthPreservesOuterCount()
    {
        NestedLengthValue value = new(new LengthFields("inner fields"));
        Assert.Equal(CborSerializer.Serialize(value).Length, CborSerializer.GetEncodedLength(value));
    }

    [Fact]
    public void SerializationCallback_NestedLengthPreservesOuterCount()
    {
        CallbackValue value = new(new CallbackPayload { QueryNestedLength = true });
        Assert.Equal(CborSerializer.Serialize(value).Length, CborSerializer.GetEncodedLength(value));
    }

    [Fact]
    public void HandWrittenException_ReleasesSink()
    {
        NestedLengthValue failed = new(new LengthFields("inner fields"), ThrowAfterNestedLength: true);
        _ = Assert.Throws<InvalidOperationException>(() => CborSerializer.GetEncodedLength(failed));
        NestedLengthValue next = new(new LengthFields("next fields"));
        Assert.Equal(CborSerializer.Serialize(next).Length, CborSerializer.GetEncodedLength(next));
        Assert.NotNull(failed.Output);
        Assert.Same(failed.Output, next.Output);
    }

    [Fact]
    public void CallbackException_ReleasesSink()
    {
        CallbackValue failed = new(new CallbackPayload { QueryNestedLength = true, ThrowAfterNestedLength = true });
        _ = Assert.Throws<InvalidOperationException>(() => CborSerializer.GetEncodedLength(failed));
        CallbackValue next = new(new CallbackPayload());
        Assert.Equal(CborSerializer.Serialize(next).Length, CborSerializer.GetEncodedLength(next));
        Assert.NotNull(failed.Output);
        Assert.Same(failed.Output, next.Output);
    }

    private static void AssertAllocationFree<T>(T value) where T : ICborType
    {
        long expected = CborSerializer.GetEncodedLength(value);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long total = 0;
        for (int i = 0; i < 1000; i++)
        {
            total += CborSerializer.GetEncodedLength(value);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected * 1000, total);
        Assert.Equal(0, allocated);
    }

    private record CallbackValue(CallbackPayload Payload) : CborRecord
    {
        public IBufferWriter<byte>? Output { get; private set; }

        public static void Write(IBufferWriter<byte> output, CallbackValue value)
        {
            value.Output = output;
            SAIB.Cbor.Cbor.Serialize(value.Payload, output);
        }
    }

    private sealed class CallbackPayload
    {
        [CborIgnore] public int Calls { get; private set; }
        [CborIgnore] public bool QueryNestedLength { get; init; }
        [CborIgnore] public bool ThrowAfterNestedLength { get; init; }

        [OnSerializing]
        public void OnSerializing()
        {
            Calls++;
            if (QueryNestedLength)
            {
                _ = CborSerializer.GetEncodedLength(new LengthFields("inner"));
            }
            if (ThrowAfterNestedLength)
            {
                throw new InvalidOperationException("Callback failed after a nested query.");
            }
        }
    }

    private record NestedLengthValue(LengthFields Inner, bool ThrowAfterNestedLength = false) : CborRecord
    {
        public IBufferWriter<byte>? Output { get; private set; }

        public static void Write(IBufferWriter<byte> output, NestedLengthValue value)
        {
            value.Output = output;
            CborWriter writer = new(output);
            writer.WriteBeginArray(2);
            writer.WriteString("a prefix long enough to be written first");
            _ = CborSerializer.GetEncodedLength(value.Inner);
            if (value.ThrowAfterNestedLength)
            {
                throw new InvalidOperationException("Writer failed after a nested query.");
            }
            LengthFields.Write(output, value.Inner);
        }
    }

}
