using System.Text.Json;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;

namespace Chrysalis.IntegrationTests;

/// <summary>Separate negative/query-mode proofs; these do not qualify the four-control version matrix.</summary>
public sealed class B1HandshakeIntegrationTests
{
    /// <summary>A real wrong-magic startup exposes the node's text and never publishes negotiation.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task WrongMagicStartupThrowsTypedNodeRefusal()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        ulong wrongMagic = Env.NetworkMagic ^ 1;
        N2CVersionTable offer = Env.CreateN2COffer(wrongMagic, Environment.GetEnvironmentVariable(Env.ForcedN2CVersion));
        await OracleCli.RecordVersionAsync(deadline.Token);
        using NodeClient node = await NodeClient.ConnectAsync(Env.Required(Env.Socket), deadline.Token);
        HandshakeRefusedException failure = await Assert.ThrowsAsync<HandshakeRefusedException>(() => node.StartAsync(offer, deadline.Token));
        Refused reason = Assert.IsType<Refused>(failure.Reason);
        Assert.Contains(reason.VersionNumber, offer.Value.Keys.Select(version => version.Value));
        Assert.Contains("networkMagic", reason.ErrorData, StringComparison.Ordinal);
        Assert.Contains(reason.ErrorData, failure.Message, StringComparison.Ordinal);
        Assert.Null(node.NegotiatedVersion);
        await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "typed-wrong-magic-observation.json"), JsonSerializer.Serialize(new
        {
            source = "SDK observation of real node refusal, not an independent CLI oracle",
            node = Env.Required(Env.NodeVersion),
            offeredMagic = wrongMagic,
            nodeMagic = Env.NetworkMagic,
            wireVersion = reason.VersionNumber,
            text = reason.ErrorData
        }), deadline.Token);
    }

    /// <summary>A real query-mode handshake returns the raw reply union instead of a usable connection.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task QueryModeReturnsNodeToClientQueryReply()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        N2CVersionTable control = Env.CreateN2COffer(Env.NetworkMagic, Environment.GetEnvironmentVariable(Env.ForcedN2CVersion));
        N2CVersionTable offer = new(control.Value.ToDictionary(pair => pair.Key, pair => new N2CVersionData(pair.Value.NetworkMagic, true)));
        await OracleCli.RecordVersionAsync(deadline.Token);
        using UnixBearer bearer = await UnixBearer.CreateAsync(Env.Required(Env.Socket), deadline.Token);
        using Plexer plexer = new(bearer);
        Task running = plexer.RunAsync(deadline.Token);
        bool receivedReply = false;
        try
        {
            Handshake handshake = new(plexer.SubscribeClient(ProtocolType.Handshake));
            N2CQueryReply reply = Assert.IsType<N2CQueryReply>(await handshake.SendAsync(HandshakeMessages.ProposeVersions(offer), deadline.Token));
            Assert.NotEmpty(reply.VersionTable.Value);
            Assert.All(reply.VersionTable.Value, pair => Assert.Equal(Env.NetworkMagic, pair.Value.NetworkMagic));
            receivedReply = true;
            await File.WriteAllTextAsync(Path.Combine(Env.OutputDirectory, "query-reply-observation.json"), JsonSerializer.Serialize(new
            {
                source = "SDK observation of real query-reply, not an independent CLI oracle",
                node = Env.Required(Env.NodeVersion),
                offeredWireVersions = offer.Value.Keys.Select(version => version.Value).Order().ToArray(),
                returnedWireVersions = reply.VersionTable.Value.Keys.Select(version => version.Value).Order().ToArray()
            }), deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            plexer.Dispose();
            try { await running; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            // Query mode is terminal: the server closes cleanly after the decoded reply.
            // Accept only that exact post-reply clean close; do not hide a pre-reply failure.
            catch (ConnectionClosedException exception) when (receivedReply && exception.InnerException is null) { }
        }
    }
}
