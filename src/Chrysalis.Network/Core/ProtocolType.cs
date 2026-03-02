namespace Chrysalis.Network.Core;

/// <summary>
/// Identifies the Ouroboros mini-protocol type used for multiplexer routing.
/// Each value corresponds to a protocol ID in the Ouroboros network multiplexer header.
/// </summary>
public enum ProtocolType
{
    /// <summary>The Handshake mini-protocol for negotiating protocol versions (protocol ID 0).</summary>
    Handshake = 0,
    /// <summary>The node-to-node ChainSync mini-protocol (protocol ID 2).</summary>
    NodeChainSync = 2,
    /// <summary>The BlockFetch mini-protocol for fetching blocks by range (protocol ID 3).</summary>
    BlockFetch = 3,
    /// <summary>The TxSubmission mini-protocol for node-to-node transaction propagation (protocol ID 4).</summary>
    TxSubmission = 4,
    /// <summary>The client-to-node ChainSync mini-protocol (protocol ID 5).</summary>
    ClientChainSync = 5,
    /// <summary>The LocalTxSubmission mini-protocol for submitting transactions locally (protocol ID 6).</summary>
    LocalTxSubmission = 6,
    /// <summary>The LocalStateQuery mini-protocol for querying ledger state (protocol ID 7).</summary>
    LocalStateQuery = 7,
    /// <summary>The KeepAlive mini-protocol for maintaining node-to-node connections (protocol ID 8).</summary>
    KeepAlive = 8,
    /// <summary>The LocalTxMonitor mini-protocol for monitoring the local mempool (protocol ID 9).</summary>
    LocalTxMonitor = 9,
    /// <summary>The PeerSharing mini-protocol for exchanging peer addresses (protocol ID 10).</summary>
    PeerSharing = 10,
}
