using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Query(
    [CborIndex(0)][ExactValue(3)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborBase QueryRequest
) : LocalStateQueryMessage;

public class QueryRequest
{
    public static Query New(CborBase queryRequest)
    {
        return new Query(new ExactValue<CborInt>(new(3)), queryRequest);
    }
}