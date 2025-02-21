namespace Chrysalis.Network.Multiplexer;

public enum ProtocolType
{
    Handshake = 0,
    NodeChainSync = 2,
    BlockFetch = 3,
    TxSubmission = 4,
    ClientChainSync = 5,
    LocalTxSubmission = 6,
    LocalStateQuery = 7,
    KeepAlive = 8,
    PeerSharing = 10,
}

public interface IMiniProtocol
{
    ProtocolType ProtocolId { get; }
}