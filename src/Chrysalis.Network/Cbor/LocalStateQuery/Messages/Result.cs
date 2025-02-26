using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Result(
    [CborIndex(0)] ResultIdx Idx,
    [CborIndex(1)] CborEncodedValue QueryResult
) : LocalStateQueryMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 4)]
public record ResultIdx(int Value) : CborBase;