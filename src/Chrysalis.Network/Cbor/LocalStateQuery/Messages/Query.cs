using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public record Query(
    [CborIndex(0)] QueryIdx Idx,
    [CborIndex(1)] CborBytes QueryRequest
) : LocalStateQueryMessage;

public class QueryRequest
{
    public static CborBytes New(CborBytes queryRequest)
    {
        byte[] request = CborSerializer.Serialize(new Query(new(3), queryRequest));
        return new CborBytes(request);
    }
}

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 3)]
public record QueryIdx(int Value) : CborBase;