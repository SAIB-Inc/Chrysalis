using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Enumerates the Cardano eras used for era-specific queries in the LocalStateQuery mini-protocol.
/// </summary>
public enum QueryEra
{
    /// <summary>The Byron era.</summary>
    Byron = 0,
    /// <summary>The Shelley era.</summary>
    Shelley = 1,
    /// <summary>The Allegra era.</summary>
    Allegra = 2,
    /// <summary>The Mary era.</summary>
    Mary = 3,
    /// <summary>The Alonzo era.</summary>
    Alonzo = 4,
    /// <summary>The Babbage era.</summary>
    Babbage = 5,
    /// <summary>The Conway era.</summary>
    Conway = 6,
}

/// <summary>
/// Provides pre-built query request objects for common LocalStateQuery mini-protocol queries.
/// </summary>
public static class RawQueries
{
    /// <summary>
    /// Gets a query request to retrieve the current Cardano era.
    /// </summary>
    public static QueryReq GetCurrentEra => new BaseQuery(0, new BaseQuery(2, new GlobalQuery(1)));

    /// <summary>
    /// Gets a query request to retrieve the current chain tip.
    /// </summary>
    public static QueryReq GetTip => CreateBlockQuery(new GlobalQuery(0));

    /// <summary>
    /// Gets a query request to retrieve the current protocol parameters.
    /// </summary>
    public static QueryReq GetCurrentProtocolParams => CreateBlockQuery(new GlobalQuery(3));

    /// <summary>
    /// Wraps a query in the block query envelope targeting the Conway era.
    /// </summary>
    /// <param name="query">The inner query to wrap.</param>
    /// <returns>A <see cref="QueryReq"/> wrapped for the Conway era block query.</returns>
    public static QueryReq CreateBlockQuery(QueryReq query)
    {
        return new BaseQuery(0, new BaseQuery(0, new BaseQuery((int)QueryEra.Conway, query)));
    }

    /// <summary>
    /// Creates a query request to retrieve UTxOs at the specified addresses.
    /// </summary>
    /// <param name="addresses">The list of serialized address bytes to query UTxOs for.</param>
    /// <returns>A <see cref="QueryReq"/> for retrieving UTxOs by address.</returns>
    public static QueryReq GetUtxoByAddress(List<ReadOnlyMemory<byte>> addresses)
    {
        return CreateBlockQuery(new UtxoByAddressQuery(6, new(addresses)));
    }

    /// <summary>
    /// Creates a query request to retrieve UTxOs for the specified transaction inputs.
    /// </summary>
    /// <param name="txIns">The list of transaction inputs to query UTxOs for.</param>
    /// <returns>A <see cref="QueryReq"/> for retrieving UTxOs by transaction input.</returns>
    public static QueryReq GetUtxoByTxIns(List<TransactionInput> txIns)
    {
        return CreateBlockQuery(new UtxoByTxInQuery(15, new(txIns)));
    }
}


/// <summary>
/// Abstract base type for all LocalStateQuery request structures, serialized as CBOR.
/// </summary>
public abstract partial record QueryReq : CborBase;

/// <summary>
/// Represents a base query wrapper in the LocalStateQuery mini-protocol that nests an inner query with a numeric selector.
/// </summary>
/// <param name="Query">The numeric query selector or era index.</param>
/// <param name="InnerQuery">The nested inner query, or null for terminal queries.</param>
[CborSerializable]
[CborList]
public partial record BaseQuery(
    [CborOrder(0)] int Query,
    [CborOrder(1)] QueryReq? InnerQuery
) : QueryReq;

/// <summary>
/// Represents a UTxO-by-address query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index for UTxO-by-address queries.</param>
/// <param name="Addresses">The list of serialized address bytes to query.</param>
[CborSerializable]
[CborList]
public partial record UtxoByAddressQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<ReadOnlyMemory<byte>> Addresses
) : QueryReq;

/// <summary>
/// Represents a UTxO-by-transaction-input query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index for UTxO-by-transaction-input queries.</param>
/// <param name="TxIns">The list of transaction inputs to query.</param>
[CborSerializable]
[CborList]
public partial record UtxoByTxInQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<TransactionInput> TxIns
) : QueryReq;

/// <summary>
/// Represents a global (era-independent) query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Query">The numeric identifier of the global query type.</param>
[CborSerializable]
[CborList]
public partial record GlobalQuery([CborOrder(0)] ulong Query) : QueryReq;
