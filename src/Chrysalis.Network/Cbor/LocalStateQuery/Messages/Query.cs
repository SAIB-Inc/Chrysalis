using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Query(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] QueryReq QueryRequest
) : LocalStateQueryMessage;

public static class QueryRequest
{
    public static Query New(QueryReq queryRequest)
    {
        return new Query(3, queryRequest);
    }
}
