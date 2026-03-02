using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Query message in the Ouroboros LocalStateQuery mini-protocol, carrying a ledger state query request.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="QueryRequest">The query request payload describing which ledger state to retrieve.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Query(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] QueryReq QueryRequest
) : LocalStateQueryMessage;

/// <summary>
/// Factory for creating Query messages in the LocalStateQuery mini-protocol.
/// </summary>
public static class QueryRequest
{
    /// <summary>
    /// Creates a new Query message with the specified query request.
    /// </summary>
    /// <param name="queryRequest">The query request payload.</param>
    /// <returns>A <see cref="Query"/> message ready to send to the Cardano node.</returns>
    public static Query New(QueryReq queryRequest)
    {
        return new Query(3, queryRequest);
    }
}
