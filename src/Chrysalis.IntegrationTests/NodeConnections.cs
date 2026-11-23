using System.Runtime.CompilerServices;
using System.Text.Json;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.IntegrationTests;

internal static class NodeConnections
{
    internal static async Task<NodeClient> OpenAsync(CancellationToken cancellationToken, [CallerMemberName] string operation = "")
    {
        N2CVersionTable offer = IntegrationEnvironment.CreateN2COffer(IntegrationEnvironment.NetworkMagic,
            Environment.GetEnvironmentVariable(IntegrationEnvironment.ForcedN2CVersion));
        await OracleCli.RecordVersionAsync(cancellationToken);
        NodeClient client = await NodeClient.ConnectAsync(IntegrationEnvironment.Required(IntegrationEnvironment.Socket), cancellationToken);
        try
        {
            await client.StartAsync(offer, cancellationToken);
            Assert.NotNull(client.NegotiatedVersion);
            await RecordNegotiationAsync(operation, offer, client.NegotiatedVersion, client.NetworkMagic, cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    internal static Task RecordNegotiationAsync(string operation, N2CVersionTable offer, N2CVersion negotiated, ulong magic, CancellationToken cancellationToken)
    {
        Assert.Contains(negotiated.Value, offer.Value.Keys.Select(version => version.Value));
        string observation = JsonSerializer.Serialize(new
        {
            source = "SDK observation, not an independent oracle",
            operation,
            node = IntegrationEnvironment.Required(IntegrationEnvironment.NodeVersion),
            offeredWireVersions = offer.Value.Keys.Select(version => version.Value).Order().ToArray(),
            negotiatedWireVersion = negotiated.Value,
            negotiatedLogicalVersion = negotiated.Value - 32768,
            networkMagic = magic
        });
        return File.AppendAllTextAsync(Path.Combine(IntegrationEnvironment.OutputDirectory, "negotiated-n2c-observations.jsonl"), observation + Environment.NewLine, cancellationToken);
    }
}

internal sealed class PeerConnection : IAsyncDisposable
{
    private readonly Plexer _plexer;
    private readonly CancellationTokenSource _stop;
    private readonly Task _running;

    private PeerConnection(IBearer bearer, CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _plexer = new Plexer(bearer);
        Handshake = new Handshake(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new ChainSync(_plexer.SubscribeClient(ProtocolType.NodeChainSync), ProtocolType.NodeChainSync);
        BlockFetch = new BlockFetch(_plexer.SubscribeClient(ProtocolType.BlockFetch));
        KeepAlive = new KeepAliveClient(_plexer.SubscribeClient(ProtocolType.KeepAlive));
        _running = _plexer.RunAsync(_stop.Token);
    }

    internal Handshake Handshake { get; }
    internal ChainSync ChainSync { get; }
    internal BlockFetch BlockFetch { get; }
    internal KeepAliveClient KeepAlive { get; }

    internal static async Task<PeerConnection> OpenAsync(CancellationToken cancellationToken)
    {
        IBearer bearer = await TcpBearer.CreateAsync(IntegrationEnvironment.RequireLoopbackHost(), IntegrationEnvironment.NodeToNodePort, cancellationToken);
        PeerConnection connection = new(bearer, cancellationToken);
        try
        {
            N2NAcceptVersion accepted = await connection.Handshake.SendAsync<N2NAcceptVersion>(
                HandshakeMessages.ProposeVersions(VersionTables.N2nV11AndAbove(IntegrationEnvironment.NetworkMagic)), cancellationToken);
            Assert.Equal(IntegrationEnvironment.NetworkMagic, accepted.VersionData.NetworkMagic);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _plexer.Dispose();
        try
        {
            await _running;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Expected lifetime cancellation after the test has finished using the peer.
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
