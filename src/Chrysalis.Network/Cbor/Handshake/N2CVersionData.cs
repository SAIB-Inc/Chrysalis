using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.Handshake;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record N2CVersionData(
    [CborIndex(0)] CborUlong NetworkMagic,
    [CborIndex(1)] CborBool? Query
) : CborBase;

