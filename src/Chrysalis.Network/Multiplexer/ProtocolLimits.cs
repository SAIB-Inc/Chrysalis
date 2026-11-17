using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

internal static class ProtocolLimits
{
    internal static int? Get(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Handshake or ProtocolType.NodeChainSync or ProtocolType.BlockFetch or
        ProtocolType.TxSubmission or ProtocolType.KeepAlive or ProtocolType.PeerSharing => 2_500_000,
        ProtocolType.ClientChainSync or ProtocolType.LocalTxSubmission or ProtocolType.LocalStateQuery or ProtocolType.LocalTxMonitor => null,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };
}
