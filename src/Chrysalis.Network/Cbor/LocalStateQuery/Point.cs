using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborConverter(typeof(CustomListConverter))]
public record Point(
    [CborIndex(0)] CborUlong Slot,
    [CborIndex(1)] CborBytes Hash
) : CborBase;
