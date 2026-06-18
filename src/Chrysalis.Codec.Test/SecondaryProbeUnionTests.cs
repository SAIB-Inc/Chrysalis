using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Codec.Test;

// Isolated coverage for the union secondary structural probe: two union members that share the
// same leading index (CborIndex 2) but differ in the CBOR shape of a later field — one carries a
// tag-24 byte string (CborEncodedValue), the other a CBOR array (a [CborList] record). The engine
// must resolve them by probing that field's shape instead of dropping the whole union to try/catch.
// This mirrors the N2N/N2C RollForward case in Chrysalis.Network.

/// <summary>An array-shaped payload: [era, #6.24(body)].</summary>
[CborSerializable]
[CborList]
public partial record ProbeHeaderPayload(
    [CborOrder(0)] int Era,
    [CborOrder(1)] CborEncodedValue Body
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

[CborSerializable]
[CborUnion]
public abstract partial record ProbeResponse : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

/// <summary>Singleton index 1.</summary>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record ProbeAwait(
    [CborOrder(0)] int Idx
) : ProbeResponse;

/// <summary>Index 2, payload is a tag-24 byte string -> resolves via ByteString.</summary>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record ProbeForwardBytes(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue Payload,
    [CborOrder(2)] int Tip
) : ProbeResponse;

/// <summary>Index 2, payload is an array -> resolves via Array. Shares the index with ProbeForwardBytes.</summary>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record ProbeForwardArray(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ProbeHeaderPayload Payload,
    [CborOrder(2)] int Tip
) : ProbeResponse;

/// <summary>Singleton index 3.</summary>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record ProbeBack(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] int Point
) : ProbeResponse;

public class SecondaryProbeUnionTests
{
    private static readonly byte[] BodyBytes = Convert.FromHexString("DEADBEEF");

    [Fact]
    public void SharedIndex_BytesPayload_ResolvesToByteStringMember()
    {
        ProbeForwardBytes original = new(2, new CborEncodedValue(BodyBytes), 99);
        byte[] bytes = CborSerializer.Serialize(original);

        ProbeResponse decoded = CborSerializer.Deserialize<ProbeResponse>(bytes);

        ProbeForwardBytes typed = Assert.IsType<ProbeForwardBytes>(decoded);
        Assert.Equal(99, typed.Tip);
        Assert.Equal(Convert.ToHexString(BodyBytes), Convert.ToHexString(typed.Payload.Value.ToArray()));
    }

    [Fact]
    public void SharedIndex_ArrayPayload_ResolvesToArrayMember()
    {
        ProbeForwardArray original = new(2, new ProbeHeaderPayload(6, new CborEncodedValue(BodyBytes)), 99);
        byte[] bytes = CborSerializer.Serialize(original);

        ProbeResponse decoded = CborSerializer.Deserialize<ProbeResponse>(bytes);

        ProbeForwardArray typed = Assert.IsType<ProbeForwardArray>(decoded);
        Assert.Equal(6, typed.Payload.Era);
        Assert.Equal(99, typed.Tip);
    }

    [Fact]
    public void Singleton_Index1_StillDispatchesByIndex()
    {
        ProbeAwait original = new(1);
        byte[] bytes = CborSerializer.Serialize(original);

        ProbeResponse decoded = CborSerializer.Deserialize<ProbeResponse>(bytes);

        _ = Assert.IsType<ProbeAwait>(decoded);
    }

    [Fact]
    public void Singleton_Index3_StillDispatchesByIndex()
    {
        ProbeBack original = new(3, 12345);
        byte[] bytes = CborSerializer.Serialize(original);

        ProbeResponse decoded = CborSerializer.Deserialize<ProbeResponse>(bytes);

        ProbeBack typed = Assert.IsType<ProbeBack>(decoded);
        Assert.Equal(12345, typed.Point);
    }

    [Fact]
    public void BothSharedIndexVariants_RoundTrip()
    {
        ProbeForwardBytes a = new(2, new CborEncodedValue(BodyBytes), 1);
        ProbeForwardArray b = new(2, new ProbeHeaderPayload(7, new CborEncodedValue(BodyBytes)), 2);

        ProbeResponse da = CborSerializer.Deserialize<ProbeResponse>(CborSerializer.Serialize(a));
        ProbeResponse db = CborSerializer.Deserialize<ProbeResponse>(CborSerializer.Serialize(b));

        _ = Assert.IsType<ProbeForwardBytes>(da);
        _ = Assert.IsType<ProbeForwardArray>(db);
    }
}
