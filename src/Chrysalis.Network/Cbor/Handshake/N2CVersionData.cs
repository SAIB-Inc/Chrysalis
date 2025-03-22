using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

[CborSerializable]
[CborList]
public partial record N2CVersionData(
    [CborOrder(0)] ulong NetworkMagic,
    [CborOrder(1)] bool? Query
) : CborBase<N2CVersionData>;

