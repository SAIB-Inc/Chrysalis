using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

[CborSerializable]
[CborList]
public partial record N2NVersionData(
    [CborOrder(0)] ulong NetworkMagic,
    [CborOrder(1)] bool InitiatorOnlyDiffusionMode,
    [CborOrder(2)] int? PeerSharing,
    [CborOrder(3)] bool? Query
) : CborBase<N2NVersionData>;

