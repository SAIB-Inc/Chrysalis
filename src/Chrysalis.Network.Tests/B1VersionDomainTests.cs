using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Wire-family discrimination and independent Haskell proposal compatibility for B1.</summary>
public sealed class B1VersionDomainTests
{
    /// <summary>N2C scalar decoding must not accept a value from the node-to-node family.</summary>
    [Theory]
    [InlineData("0e")]
    [InlineData("1a00010000")]
    public void NodeToClientRejectsWrongWireFamily(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<N2CVersion>(Convert.FromHexString(hex)));

    /// <summary>N2N scalar decoding must not accept a node-to-client value or a non-Word16 value.</summary>
    [Theory]
    [InlineData("198017")]
    [InlineData("1a00010000")]
    public void NodeToNodeRejectsWrongWireFamily(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<N2NVersion>(Convert.FromHexString(hex)));

    /// <summary>Unknown future numbers inside the correct family remain representable explicitly.</summary>
    [Fact]
    public void FutureNumbersInsideTheWireFamilyRemainRepresentable()
    {
        Assert.Equal(32792, CborSerializer.Deserialize<N2CVersion>(Convert.FromHexString("198018")).Value);
        Assert.Equal(15, CborSerializer.Deserialize<N2NVersion>(Convert.FromHexString("0f")).Value);
    }

    /// <summary>The reply union must use the family bit rather than whichever structural shape matches first.</summary>
    [Fact]
    public void QueryReplyClassifiesItsVersionTableAsNodeToClient()
    {
        byte[] frame = Convert.FromHexString("8203a119801782182af4"); // SYNTHETIC: pinned OND CDDL, [3,{32791:[42,false]}].
        N2CQueryReply reply = Assert.IsType<N2CQueryReply>(CborSerializer.Deserialize<HandshakeMessage>(frame));
        KeyValuePair<N2CVersion, N2CVersionData> entry = Assert.Single(reply.VersionTable.Value);
        Assert.Equal(32791, entry.Key.Value);
        Assert.Equal(42UL, entry.Value.NetworkMagic);
        Assert.False(entry.Value.Query);
    }

    /// <summary>Every independent G1 node accept must retain the N2C family at the generic union boundary.</summary>
    /// <param name="file">An unchanged Haskell node accept capture.</param>
    [Theory]
    [InlineData("c1-in-p0-001.cbor")]
    [InlineData("c2-in-p0-001.cbor")]
    [InlineData("c3-in-p0-001.cbor")]
    [InlineData("c4-in-p0-001.cbor")]
    [InlineData("c5-in-p0-001.cbor")]
    [InlineData("c6-in-p0-001.cbor")]
    [InlineData("c7-in-p0-001.cbor")]
    [InlineData("c8-in-p0-001.cbor")]
    [InlineData("c9-in-p0-001.cbor")]
    [InlineData("c10-in-p0-001.cbor")]
    [InlineData("c11-in-p0-001.cbor")]
    [InlineData("c12-in-p0-001.cbor")]
    [InlineData("c13-in-p0-001.cbor")]
    public void IndependentAcceptsAreNodeToClientAtTheUnionBoundary(string file)
    {
        byte[] frame = PreprodFixtures.Read("wire/" + file);
        N2CAcceptVersion reply = Assert.IsType<N2CAcceptVersion>(CborSerializer.Deserialize<HandshakeMessage>(frame));
        Assert.InRange(reply.Version.Value, 32784, 32791);
        Assert.Equal(1UL, reply.VersionData.NetworkMagic);
        Assert.False(reply.VersionData.Query);
    }

    /// <summary>A freshly encoded SDK offer subset must equal both independent Haskell client proposals.</summary>
    /// <param name="file">The original Haskell CLI proposal.</param>
    [Theory]
    [InlineData("c4-out-p0-001.cbor")]
    [InlineData("c5-out-p0-001.cbor")]
    public void OfferSubsetReproducesIndependentHaskellCliProposal(string file)
    {
        N2CVersionTable defaults = VersionTables.N2cV10AndAbove(1);
        N2CVersionTable subset = new(defaults.Value.Where(entry => entry.Key.Value <= 32790).ToDictionary());
        byte[] actual = CborSerializer.Serialize(HandshakeMessages.ProposeVersions(subset));
        Assert.Equal(PreprodFixtures.Read("wire/" + file), actual);
    }

    /// <summary>The generic convenience path must expose a refusal, not try to decode it as acceptance.</summary>
    [Fact]
    public async Task GenericHandshakeSurfacesTypedVersionMismatch()
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString("8202820081198017"));
        Handshake handshake = new(replay.Channel);
        ProposeVersions proposal = HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove(42));
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => handshake.SendAsync<N2CAcceptVersion>(proposal, CancellationToken.None));
        // Reflection keeps this pre-implementation red executable rather than failing to compile on a missing API.
        Assert.Equal("HandshakeRefusedException", failure.GetType().Name);
        System.Reflection.PropertyInfo? property = failure.GetType().GetProperty("Reason");
        Assert.NotNull(property);
        VersionMismatch mismatch = Assert.IsType<VersionMismatch>(property.GetValue(failure));
        Assert.Equal([32791], mismatch.VersionNumbers.Value);
    }
}
