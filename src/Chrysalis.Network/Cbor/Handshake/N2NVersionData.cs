using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// CBOR-encoded parameters for a node-to-node protocol version, exchanged during the Handshake mini-protocol.
/// </summary>
/// <param name="NetworkMagic">The network magic number identifying the Cardano network (e.g., mainnet, testnet).</param>
/// <param name="InitiatorOnlyDiffusionMode">Whether the node operates in initiator-only diffusion mode.</param>
/// <param name="PeerSharing">The peer sharing mode (0 = no sharing). Available from protocol version 11 onward.</param>
/// <param name="Query">Whether this connection is a version query only. If true, the connection will be closed after the handshake.</param>
[CborSerializable]
[CborList]
public partial record N2NVersionData(
    [CborOrder(0)] ulong NetworkMagic,
    [CborOrder(1)] bool InitiatorOnlyDiffusionMode,
    [CborOrder(2)] int? PeerSharing,
    [CborOrder(3)] bool? Query
) : CborBase;
