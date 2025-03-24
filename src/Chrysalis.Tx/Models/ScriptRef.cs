using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Tx.Models;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ScriptRef(
    [CborIndex(0)] CborInt Type,
    [CborIndex(1)] CborBytes ScriptBytes
) : CborBase;
