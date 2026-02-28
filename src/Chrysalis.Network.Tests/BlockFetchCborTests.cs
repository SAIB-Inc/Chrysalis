using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.Common;
using Xunit;

namespace Chrysalis.Network.Tests;

public class BlockFetchCborTests
{
    [Fact]
    public void RequestRange_Encodes_Correctly()
    {
        // RequestRange with two origin points should be [0, [], []]
        RequestRange message = BlockFetchMessages.RequestRange(Point.Origin, Point.Origin);
        byte[] actual = CborSerializer.Serialize(message);

        // CBOR: array(3) [ 0, array(0), array(0) ]
        // 83 = array of 3
        // 00 = uint 0
        // 80 = array of 0 (Origin)
        // 80 = array of 0 (Origin)
        byte[] expected = [0x83, 0x00, 0x80, 0x80];
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(actual));
    }

    [Fact]
    public void RequestRange_With_SpecificPoint_Encodes_Correctly()
    {
        byte[] hash = new byte[32];
        hash[0] = 0xAB;
        hash[31] = 0xCD;
        Point point = Point.Specific(42, hash);
        RequestRange message = BlockFetchMessages.RequestRange(point, point);
        byte[] actual = CborSerializer.Serialize(message);

        // Should be array(3): [0, [42, h'AB00...CD'], [42, h'AB00...CD']]
        // Verify it starts with array(3) and discriminant 0
        Assert.Equal(0x83, actual[0]); // array of 3
        Assert.Equal(0x00, actual[1]); // discriminant 0
    }

    [Fact]
    public void RequestRange_With_Origin_RoundTrips()
    {
        // Manually build expected: [0, [], []] = 83 00 80 80
        byte[] expected = [0x83, 0x00, 0x80, 0x80];
        BlockFetchMessage decoded = CborSerializer.Deserialize<BlockFetchMessage>(expected);
        RequestRange rr = Assert.IsType<RequestRange>(decoded);
        OriginPoint _ = Assert.IsType<OriginPoint>(rr.From);
        OriginPoint __ = Assert.IsType<OriginPoint>(rr.To);
    }

    [Fact]
    public void RequestRange_Hex_Dump()
    {
        byte[] hash = Convert.FromHexString("CD619529CA62B4C37F7F728CD6D3472682115F001E1D1278BF1B7DCE528DB44E");
        Point point = Point.Specific(20, hash);
        RequestRange message = BlockFetchMessages.RequestRange(point, point);
        byte[] actual = CborSerializer.Serialize(message);
        // Just print it for debugging
        Assert.NotEmpty(actual);
        // Verify structure: array(3), 0, array(2), ..., array(2), ...
        Assert.Equal(0x83, actual[0]); // array(3)
        Assert.Equal(0x00, actual[1]); // 0
        Assert.Equal(0x82, actual[2]); // array(2) for first point
    }

    [Fact]
    public void ClientDone_Encodes_Correctly()
    {
        ClientDone message = BlockFetchMessages.ClientDone();
        byte[] actual = CborSerializer.Serialize(message);

        // [1] -> 81 01
        byte[] expected = [0x81, 0x01];
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(actual));
    }

    [Fact]
    public void StartBatch_Decodes_Correctly()
    {
        // [2] -> 81 02
        byte[] cbor = [0x81, 0x02];
        BlockFetchMessage message = CborSerializer.Deserialize<BlockFetchMessage>(cbor);
        StartBatch _ = Assert.IsType<StartBatch>(message);
    }

    [Fact]
    public void NoBlocks_Decodes_Correctly()
    {
        // [3] -> 81 03
        byte[] cbor = [0x81, 0x03];
        BlockFetchMessage message = CborSerializer.Deserialize<BlockFetchMessage>(cbor);
        NoBlocks _ = Assert.IsType<NoBlocks>(message);
    }

    [Fact]
    public void BatchDone_Decodes_Correctly()
    {
        // [5] -> 81 05
        byte[] cbor = [0x81, 0x05];
        BlockFetchMessage message = CborSerializer.Deserialize<BlockFetchMessage>(cbor);
        BatchDone _ = Assert.IsType<BatchDone>(message);
    }
}
