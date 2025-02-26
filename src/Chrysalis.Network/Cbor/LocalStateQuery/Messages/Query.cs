using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Query(
    [CborIndex(0)] QueryIdx Idx,
    [CborIndex(1)] CborBase QueryRequest
) : LocalStateQueryMessage;

public class QueryRequest
{
    public static byte[] New(CborBase queryRequest)
    {
        return CborSerializer.Serialize(new Query(new(3), queryRequest));
    }
}

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 3)]
public record QueryIdx(int Value) : CborBase;