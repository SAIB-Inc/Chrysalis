using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>
/// Deserialization tests for the unified ChainSync next-response union — no node, no multiplexer,
/// just bytes. N2C and N2N RollForward share chain-sync index 2 inside a single
/// <see cref="MessageNextResponse"/> union; the serializer's structural probe resolves them by the
/// payload's CBOR shape: a tag-24 block (<see cref="N2CMessageRollForward"/>) vs. an
/// <c>[era, #6.24(header)]</c> array (<see cref="N2NMessageRollForward"/>).
/// </summary>
public class N2NRollForwardCborTests
{
    // A 32-byte stand-in for header/block content (real payloads are larger; shape is what matters).
    private static readonly byte[] HeaderBytes =
        Convert.FromHexString("7EF942E6A670AF6310737E9230B22E11A4BB1AF69BED9AFFB09B1025B371D1CD");

    // The N2N payload: 82 (array2) 06 (era=6 Conway) D818 (tag24) 5820 (bstr len 32) <32 bytes>.
    private static byte[] BuildN2nPayload() =>
        [0x82, 0x06, 0xD8, 0x18, 0x58, 0x20, .. HeaderBytes];

    [Fact]
    public void N2NBlockHeader_DeserializesCleanly_AsArrayEraPlusEncodedHeader()
    {
        byte[] payload = BuildN2nPayload();

        N2NBlockHeader decoded = CborSerializer.Deserialize<N2NBlockHeader>(payload);

        Assert.Equal(6, decoded.EraTag);
        Assert.Equal(
            Convert.ToHexString(HeaderBytes),
            Convert.ToHexString(decoded.HeaderCbor.Value.ToArray()));
    }

    [Fact]
    public void SharedUnion_ResolvesN2NRollForward_ByArrayPayload()
    {
        Tip tip = new(Point.Specific(126025649UL, HeaderBytes), 4833605);
        N2NMessageRollForward original = new(2, new N2NBlockHeader(6, new CborEncodedValue(HeaderBytes)), tip);
        byte[] bytes = CborSerializer.Serialize(original);

        // Deserialize against the SHARED union — the structural probe must pick the N2N (array) member.
        MessageNextResponse decoded = CborSerializer.Deserialize<MessageNextResponse>(bytes);

        N2NMessageRollForward typed = Assert.IsType<N2NMessageRollForward>(decoded);
        Assert.Equal(6, typed.Payload.EraTag);
        Assert.Equal(126025649UL, ((SpecificPoint)typed.Tip.Slot).Slot);
    }

    [Fact]
    public void SharedUnion_ResolvesN2CRollForward_ByTag24Payload()
    {
        Tip tip = new(Point.Specific(126025649UL, HeaderBytes), 4833605);
        N2CMessageRollForward original = new(2, new CborEncodedValue(HeaderBytes), tip);
        byte[] bytes = CborSerializer.Serialize(original);

        // Same union, same index 2 — but a tag-24 byte-string payload must resolve to the N2C member.
        MessageNextResponse decoded = CborSerializer.Deserialize<MessageNextResponse>(bytes);

        N2CMessageRollForward typed = Assert.IsType<N2CMessageRollForward>(decoded);
        Assert.Equal(
            Convert.ToHexString(HeaderBytes),
            Convert.ToHexString(typed.Payload.Value.ToArray()));
    }

    [Fact]
    public void IntersectFound_StillResolves_ThroughChainSyncMessage()
    {
        // FindIntersection deserializes a ChainSyncMessage and expects MessageIntersectFound (index 5).
        // With two index-2 RollForward members now in the shared union, the structural probe must not
        // disturb dispatch of the other indices — this is the regression guard for the bug that broke
        // FindIntersection when the unions were merged naively.
        MessageIntersectFound found = new(5, Point.Specific(126025608UL, HeaderBytes), new Tip(Point.Specific(126040000UL, HeaderBytes), 4900000));
        byte[] bytes = CborSerializer.Serialize(found);

        ChainSyncMessage decoded = CborSerializer.Deserialize<ChainSyncMessage>(bytes);

        _ = Assert.IsType<MessageIntersectFound>(decoded);
    }

    [Fact]
    public void FullN2NRollForwardMessage_RoundTrips_AndExposesSlotAndHeader()
    {
        // The whole message: [2, [6, #6.24(header)], tip] where tip = [[slot, hash], blockNo].
        Tip tip = new(Point.Specific(126025649UL, HeaderBytes), 4833605);
        N2NBlockHeader payload = new(6, new CborEncodedValue(HeaderBytes));
        N2NMessageRollForward original = new(2, payload, tip);

        byte[] bytes = CborSerializer.Serialize(original);
        N2NMessageRollForward decoded = CborSerializer.Deserialize<N2NMessageRollForward>(bytes);

        Assert.Equal(2, decoded.Idx);
        Assert.Equal(6, decoded.Payload.EraTag);
        Assert.Equal(Convert.ToHexString(HeaderBytes), Convert.ToHexString(decoded.Payload.HeaderCbor.Value.ToArray()));
        Assert.Equal(126025649UL, ((SpecificPoint)decoded.Tip.Slot).Slot);
    }
}
