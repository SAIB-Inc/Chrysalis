using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

public class NodeClient : IDisposable
{
    private readonly Plexer _plexer;
    private AgentChannel _handshakeChannel;

    #region MiniProtocols
    public Handshake<object> Handshake { get; private set; }
    #endregion

    private NodeClient(IBearer bearer)
    {
        _plexer = new(bearer);
        _handshakeChannel = _plexer.SubscribeClient(ProtocolType.Handshake);
        Handshake = new(_handshakeChannel);
    }

    private Aff<Unit> Start() =>
        from _ in _plexer.Run()
        select unit;

    public static Aff<NodeClient> Connect(string socketPath) =>
        from bearer in UnixBearer.Create(socketPath)
        let client = new NodeClient(bearer)
        from _ in client.Start()
        from delay in Aff(async () => {  await Task.Delay(1); return unit; }) // @TODO: Remove this hack
        from __ in client.Handshake.Send()
        select client;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plexer.Dispose();
    }
}