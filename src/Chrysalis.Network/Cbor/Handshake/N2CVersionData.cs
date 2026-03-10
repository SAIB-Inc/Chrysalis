using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// CBOR-encoded parameters for a node-to-client protocol version, exchanged during the Handshake mini-protocol.
/// </summary>
/// <param name="NetworkMagic">The network magic number identifying the Cardano network (e.g., mainnet, testnet).</param>
/// <param name="Query">Whether this connection is a version query only. If true, the connection will be closed after the handshake.</param>
[CborSerializable]
[CborList]
public partial record N2CVersionData(
    [CborOrder(0)] ulong NetworkMagic,
    [CborOrder(1)] bool? Query
) : CborRecord;
