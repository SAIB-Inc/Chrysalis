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
/// <c>[era, header]</c> array (<see cref="N2NMessageRollForward"/>). The N2N header is era-shaped —
/// Shelley-and-later carry the tag-24 header directly, Byron nests it inside a bare array — so these
/// also guard that <em>both</em> shapes deserialize without throwing and round-trip via their raw bytes.
/// </summary>
public class N2NRollForwardCborTests
{
    // A 32-byte stand-in for header content (real headers are larger; shape is what matters).
    private static readonly byte[] HeaderBytes =
        Convert.FromHexString("7EF942E6A670AF6310737E9230B22E11A4BB1AF69BED9AFFB09B1025B371D1CD");

    // Shelley+ N2N payload: 82 (array2) 06 (era=6 Conway) D818 (tag24) 5820 (bstr32) <32 bytes>.
    private static byte[] BuildShelleyPayload() =>
        [0x82, 0x06, 0xD8, 0x18, 0x58, 0x20, .. HeaderBytes];

    // Byron N2N payload: 82 (array2) 00 (era=0) 82 (array2) [82 00 18 20] (=[subTag 0, size 32])
    //                    D818 (tag24) 5820 (bstr32) <32 bytes>. The tag-24 header is nested one
    //                    level deeper than Shelley+ — the exact shape that broke the old decoder.
    private static byte[] BuildByronPayload() =>
        [0x82, 0x00, 0x82, 0x82, 0x00, 0x18, 0x20, 0xD8, 0x18, 0x58, 0x20, .. HeaderBytes];

    // Tip = [[slot, hash32], blockNo]: 82 [82 1A <slot=126025649> 5820 <32>] 1A <blockNo>.
    private static byte[] BuildTip() =>
        [0x82, 0x82, 0x1A, 0x07, 0x82, 0xFF, 0xB1, 0x58, 0x20, .. HeaderBytes, 0x1A, 0x00, 0x49, 0xC0, 0x85];

    // Full RollForward message: 83 (array3) 02 (idx) <payload> <tip>.
    private static byte[] BuildRollForward(byte[] payload) =>
        [0x83, 0x02, .. payload, .. BuildTip()];

    [Fact]
    public void N2NBlockHeader_ShelleyPayload_DeserializesAndRoundTrips()
    {
        byte[] payload = BuildShelleyPayload();

        N2NBlockHeader decoded = CborSerializer.Deserialize<N2NBlockHeader>(payload);

        Assert.Equal(6, decoded.EraTag);
        // The lazy union reader captures the full [era, header] bytes; re-serialization is byte-exact.
        Assert.Equal(Convert.ToHexString(payload), Convert.ToHexString(decoded.Raw.ToArray()));
        Assert.Equal(Convert.ToHexString(payload), Convert.ToHexString(CborSerializer.Serialize(decoded)));
    }

    [Fact]
    public void N2NBlockHeader_ByronPayload_DeserializesWithoutThrowing()
    {
        // The regression guard for the infinite-hang bug: the Byron header is a bare array, not a
        // tag-24 byte string, so the old CborEncodedValue-typed field threw "Expected major type
        // ByteString". The era-discriminated union must now parse it cleanly and preserve its bytes.
        byte[] payload = BuildByronPayload();

        N2NBlockHeader decoded = CborSerializer.Deserialize<N2NBlockHeader>(payload);

        Assert.Equal(0, decoded.EraTag);
        Assert.Equal(Convert.ToHexString(payload), Convert.ToHexString(decoded.Raw.ToArray()));
    }

    [Fact]
    public void SharedUnion_ResolvesN2NRollForward_ByArrayPayload_BothEras()
    {
        foreach (byte[] payload in new[] { BuildShelleyPayload(), BuildByronPayload() })
        {
            byte[] bytes = BuildRollForward(payload);

            // Deserialize against the SHARED union — the structural probe must pick the N2N (array) member.
            MessageNextResponse decoded = CborSerializer.Deserialize<MessageNextResponse>(bytes);

            N2NMessageRollForward typed = Assert.IsType<N2NMessageRollForward>(decoded);
            Assert.Equal(2, typed.Idx);
            // The payload's raw bytes route through the era-aware decoder for the chain point.
            Assert.Equal(Convert.ToHexString(payload), Convert.ToHexString(typed.Payload.Raw.ToArray()));
            Assert.Equal(126025649UL, ((SpecificPoint)typed.Tip.Slot).Slot);
        }
    }

    [Fact]
    public void SharedUnion_ResolvesN2CRollForward_ByTag24Payload()
    {
        N2CMessageRollForward original = new(2, new CborEncodedValue(HeaderBytes), new Tip(Point.Specific(126025649UL, HeaderBytes), 4833605));
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
    public void FullN2NRollForwardMessage_RoundTrips_AndExposesEraAndSlot()
    {
        // The whole message: [2, [6, #6.24(header)], tip] where tip = [[slot, hash], blockNo].
        byte[] bytes = BuildRollForward(BuildShelleyPayload());

        N2NMessageRollForward decoded = CborSerializer.Deserialize<N2NMessageRollForward>(bytes);

        Assert.Equal(2, decoded.Idx);
        Assert.Equal(6, decoded.Payload.EraTag);
        Assert.Equal(126025649UL, ((SpecificPoint)decoded.Tip.Slot).Slot);
        Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(CborSerializer.Serialize(decoded)));
    }
}
