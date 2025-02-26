using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public record IsMessageDone(
    [CborIndex(0)] IsMessageDoneIdx Idx
) : LocalStateQueryMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 7)]
public record IsMessageDoneIdx(int Value) : CborBase;