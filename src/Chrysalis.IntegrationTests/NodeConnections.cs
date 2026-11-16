using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.IntegrationTests;

internal static class NodeConnections
{
    internal static async Task<NodeClient> OpenAsync(CancellationToken cancellationToken)
    {
        await OracleCli.RecordVersionAsync(cancellationToken);
        NodeClient client = await NodeClient.ConnectAsync(IntegrationEnvironment.Required(IntegrationEnvironment.Socket), cancellationToken);
        try
        {
            await client.StartAsync(IntegrationEnvironment.NetworkMagic).WaitAsync(cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
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
            Xunit.Assert.Equal(IntegrationEnvironment.NetworkMagic, accepted.VersionData.NetworkMagic);
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
