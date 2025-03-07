
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

public class NodeClient : IDisposable
{
    private readonly Plexer _plexer;
    #region MiniProtocols
    public Handshake? Handshake { get; private set; }
    // public LocalStateQuery LocalStateQuery { get; private set; } 
    public ChainSync? ChainSync { get; private set; }
    public LocalTxMonitor? LocalTxMonitor { get; private set; }
    #endregion

    private NodeClient(Plexer plexer)
    {
        _plexer = plexer;
    }

    public static async Task<NodeClient> ConnectAsync(string socketPath)
    {
        UnixBearer unixBearer = await UnixBearer.CreateAsync(socketPath);
        Plexer plexer = new(unixBearer);
        return new(plexer);
    }

    public void Start()
    {
        _ = _plexer.RunAsync(CancellationToken.None);
        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.ClientChainSync));
        LocalTxMonitor = new(_plexer.SubscribeClient(ProtocolType.LocalTxMonitor));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plexer.Dispose();
    }
}