using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record Query(
    [CborOrder(0)] Value3 Idx,
    [CborOrder(1)] QueryReq QueryRequest
) : LocalStateQueryMessage;

public static class QueryRequest
{
    public static Query New(QueryReq queryRequest)
    {
        return new Query(new Value3(3), queryRequest);
    }
}
