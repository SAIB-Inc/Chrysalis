using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public record IsDone(
    [CborIndex(0)] IsDoneIdx Idx
) : LocalStateQueryMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 7)]
public record IsDoneIdx(int Value) : CborBase;