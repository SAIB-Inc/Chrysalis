using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>CDDL-valid empty query tables need proposal context, not a guessed wire family.</summary>
public sealed class B1QueryReplyFamilyTests
{
    private const string EmptyReply = "8203a0"; // SYNTHETIC pinned handshake CDDL: [3, {}].
    private const string NodeToClientReply = "8203a119801782182af4"; // [3, {32791: [42,false]}].
    private const string NodeToNodeReply = "8203a10e84182af500f4"; // [3, {14: [42,true,0,false]}].

    /// <summary>The reviewer probe's N2C V23 query must accept an empty reply.</summary>
    [Fact]
    public async Task EmptyQueryReplyIsUsableByTypedNodeToClientHandshake()
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(EmptyReply));
        Handshake handshake = new(replay.Channel);
        N2CQueryReply reply = await handshake.SendAsync<N2CQueryReply>(Proposal(true), CancellationToken.None);
        Assert.Empty(reply.VersionTable.Value);
        Assert.True(handshake.IsDone);
        Assert.Equal(Convert.FromHexString(EmptyReply), CborSerializer.Serialize(reply));
    }

    /// <summary>The PeerClient/N2N family must also retain the same valid empty reply.</summary>
    [Fact]
    public async Task EmptyQueryReplyIsUsableByTypedNodeToNodeHandshake()
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(EmptyReply));
        Handshake handshake = new(replay.Channel);
        N2NQueryReply reply = await handshake.SendAsync<N2NQueryReply>(Proposal(false), CancellationToken.None);
        Assert.Empty(reply.VersionTable.Value);
        Assert.True(handshake.IsDone);
        Assert.Equal(Convert.FromHexString(EmptyReply), CborSerializer.Serialize(reply));
    }

    /// <summary>The proposal is context even when the caller requests the common reply base.</summary>
    /// <param name="nodeToClient">The family of the proposed version table.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyGenericBaseReplyUsesTheProposalFamily(bool nodeToClient)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(EmptyReply));
        Handshake handshake = new(replay.Channel);
        HandshakeMessage reply = await handshake.SendAsync<HandshakeMessage>(Proposal(nodeToClient), CancellationToken.None);
        Assert.Equal(nodeToClient ? typeof(N2CQueryReply) : typeof(N2NQueryReply), reply.GetType());
    }

    /// <summary>A requested type cannot silently override the family that was actually proposed.</summary>
    /// <param name="nodeToClient">The proposal family; the expected type deliberately requests the other.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyGenericReplyDoesNotInferItsFamilyFromTheRequestedType(bool nodeToClient)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(EmptyReply));
        Handshake handshake = new(replay.Channel);
        ProposeVersions proposal = Proposal(nodeToClient);
        if (nodeToClient)
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.SendAsync<N2NQueryReply>(proposal, CancellationToken.None));
        }
        else
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.SendAsync<N2CQueryReply>(proposal, CancellationToken.None));
        }
    }

    /// <summary>The raw API keeps the context-free decoder's ambiguous union alternative unchanged.</summary>
    /// <param name="nodeToClient">Either proposal leaves raw API interpretation unchanged.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyRawReplyDoesNotGuessTheProposalFamily(bool nodeToClient)
    {
        byte[] frame = Convert.FromHexString(EmptyReply);
        Assert.Empty(CborSerializer.Deserialize<N2CQueryReply>(frame).VersionTable.Value);
        Assert.Empty(CborSerializer.Deserialize<N2NQueryReply>(frame).VersionTable.Value);
        HandshakeMessage decoded = CborSerializer.Deserialize<HandshakeMessage>(frame);
        using ProtocolReplay replay = new(ProtocolType.Handshake, frame);
        Handshake handshake = new(replay.Channel);
        HandshakeMessage reply = await handshake.SendAsync(Proposal(nodeToClient), CancellationToken.None);
        Assert.Equal(decoded.GetType(), reply.GetType());
        Assert.Equal(frame, CborSerializer.Serialize(reply));
    }

    /// <summary>Nonempty tables continue to use actual wire keys and their existing family validators.</summary>
    /// <param name="nodeToClient">The wire/proposal family for the nonempty table.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonEmptyQueryRepliesRetainTheirWireFamily(bool nodeToClient)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(nodeToClient ? NodeToClientReply : NodeToNodeReply));
        Handshake handshake = new(replay.Channel);
        if (nodeToClient)
        {
            N2CQueryReply reply = await handshake.SendAsync<N2CQueryReply>(Proposal(true), CancellationToken.None);
            Assert.Equal(32791, Assert.Single(reply.VersionTable.Value).Key.Value);
        }
        else
        {
            N2NQueryReply reply = await handshake.SendAsync<N2NQueryReply>(Proposal(false), CancellationToken.None);
            Assert.Equal(14, Assert.Single(reply.VersionTable.Value).Key.Value);
        }
    }

    /// <summary>Proposal context must not relabel a nonempty table from the other wire family.</summary>
    /// <param name="nodeToClient">The proposal family; the wire table deliberately belongs to the other.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonEmptyQueryRepliesCannotBeRelabeledByTheProposal(bool nodeToClient)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString(nodeToClient ? NodeToNodeReply : NodeToClientReply));
        Handshake handshake = new(replay.Channel);
        if (nodeToClient)
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.SendAsync<N2CQueryReply>(Proposal(true), CancellationToken.None));
        }
        else
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handshake.SendAsync<N2NQueryReply>(Proposal(false), CancellationToken.None));
        }
    }

    /// <summary>A mixed N2N/N2C map remains invalid rather than becoming valid through proposal context.</summary>
    [Fact]
    public void MixedQueryReplyTablesRemainInvalid()
    {
        byte[] frame = Convert.FromHexString("8203a20e84182af500f419801782182af4"); // SYNTHETIC mixed-family map from the two CDDL shapes above.
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<HandshakeMessage>(frame));
    }

    private static ProposeVersions Proposal(bool nodeToClient) => nodeToClient
        ? HandshakeMessages.ProposeVersions(new N2CVersionTable(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V23] = new(42, true) }))
        : HandshakeMessages.ProposeVersions(new N2NVersionTable(new Dictionary<N2NVersion, N2NVersionData> { [N2NVersions.V14] = new(42, true, 0, true) }));
}
