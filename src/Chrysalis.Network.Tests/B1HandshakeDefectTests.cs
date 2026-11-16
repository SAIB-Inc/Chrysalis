using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.Network.Tests;

public class B1HandshakeDefectTests
{
    [Fact]
    public void DefaultNodeToClientOfferIncludesV16ThroughV23()
    {
        N2CVersionTable table = VersionTables.N2cV10AndAbove(1);
        Assert.Equal(Enumerable.Range(32784, 8), table.Value.Keys.Select(version => version.Value).Order());
        Assert.All(table.Value.Values, data => Assert.Equal(1UL, data.NetworkMagic));
    }

    // SYNTHETIC, not a node capture. Source: IntersectMBO/ouroboros-network
    // commit 34831d62d6387c2d827351785adbac06fabb1502,
    // ouroboros-network/framework/lib/Ouroboros/Network/Protocol/Handshake/Codec.hs:244-287.
    // MsgRefuse is [2, reason]; DecodeError/Refused carry CBOR TEXT, not a bytestring.
    [Theory]
    [InlineData("8202820081198017", "VersionMismatch")]
    [InlineData("82028301198017706261642076657273696f6e2064617461", "HandshakeDecodeError")]
    [InlineData("82028302198017781e6e6574776f726b206d61676963206d69736d617463683a2031202f3d2032", "Refused")]
    public void SyntheticRefusalDecodesAsTypedHandshakeReply(string hex, string reasonType)
    {
        byte[] bytes = Convert.FromHexString(hex);
        HandshakeMessage message = CborSerializer.Deserialize<HandshakeMessage>(bytes, out int consumed);
        Refuse refusal = Assert.IsType<Refuse>(message);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(2, refusal.Idx);
        Assert.Equal(reasonType, refusal.Reason.GetType().Name);
        Assert.Equal(bytes.AsSpan(2).ToArray(), CborSerializer.Serialize(refusal.Reason));
    }
}
