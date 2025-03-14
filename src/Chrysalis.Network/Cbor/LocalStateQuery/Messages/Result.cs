using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Result(
    [CborIndex(0)][ExactValue(4)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborEncodedValue QueryResult
) : LocalStateQueryMessage;

