using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public record Release(
    [CborIndex(0)] ReleaseIdx Idx
) : LocalStateQueryMessage;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 5)]
public record ReleaseIdx(int Value) : CborBase;