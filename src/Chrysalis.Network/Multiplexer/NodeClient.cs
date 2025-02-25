using Chrysalis.Network.Cbor;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using LanguageExt.ClassInstances.Const;

namespace Chrysalis.Network.Multiplexer;

public class NodeClient : IDisposable
{
    private readonly Plexer _plexer;
    #region MiniProtocols
    public Option<Handshake> Handshake { get; private set; } = None;
    public Option<LocalStateQuery> LocalStateQuery { get; private set; } = None;
    public Option<ChainSync> ChainSync { get; private set; } = None;
    #endregion

    private NodeClient(Plexer plexer)
    {
        _plexer = plexer;
    }

    private Aff<Unit> Start() =>
        from _ in _plexer.Run()
        let handshakeAgent = _plexer.SubscribeClient(ProtocolType.Handshake)
        let localStateQueryAgent = _plexer.SubscribeClient(ProtocolType.LocalStateQuery)
        from __ in Aff(() =>
        {
            Handshake = Some(new Handshake(handshakeAgent));
            LocalStateQuery = Some(new LocalStateQuery(localStateQueryAgent));
            ChainSync = Some(new ChainSync(_plexer.SubscribeClient(ProtocolType.ClientChainSync)));
            return ValueTask.FromResult(unit);
        })
        select unit;

    public static Aff<NodeClient> Connect(string socketPath) =>
        from bearer in UnixBearer.Create(socketPath)
        let plexer = new Plexer(bearer)
        let client = new NodeClient(plexer)
        from _ in client.Start()
        from __ in client.Handshake
             .IfNone(() => throw new Exception("Handshake missing"))
             .Send(HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE))
        select client;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plexer.Dispose();
    }
}