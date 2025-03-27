using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record Query(
    [CborOrder(0)] Value3 Idx,
    [CborOrder(1)] BlockQuery QueryRequest
) : LocalStateQueryMessage;

public class QueryRequest
{
    public static Query New(BlockQuery queryRequest)
    {
        return new Query(new Value3(3), queryRequest);
    }
}