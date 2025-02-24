using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record N2NVersionData(
    [CborIndex(0)] CborUlong NetworkMagic,
    [CborIndex(1)] CborBool InitiatorOnlyDiffusionMode,
    [CborIndex(2)] CborInt? PeerSharing,
    [CborIndex(3)] CborBool? Query
) : CborBase;

