using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Enumerates the Cardano eras by index; the builders of <see cref="RawQueries"/> take <see cref="Era"/> instead.
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
    public static IQueryReq GetCurrentEra => new BaseQuery(0, new BaseQuery(2, new GlobalQuery(1)));

    /// <summary>
    /// Gets a query request to retrieve the system start: the UTC time of slot zero. It is not era-specific.
    /// </summary>
    public static IQueryReq GetSystemStart => new GlobalQuery(1);

    /// <summary>
    /// Gets a query request to retrieve the era history: one summary per era the ledger knows. It is not era-specific.
    /// </summary>
    public static IQueryReq GetEraHistory => new BaseQuery(0, new BaseQuery(2, new GlobalQuery(0)));

    /// <summary>
    /// Gets a query request to retrieve the current chain tip.
    /// </summary>
    /// <param name="era">The ledger era of the acquired state, Shelley or later, as the current-era query answered it.</param>
    public static IQueryReq GetTip(Era era) => CreateBlockQuery(new GlobalQuery(0), era);

    /// <summary>
    /// Gets a query request to retrieve the current protocol parameters.
    /// </summary>
    /// <param name="era">The ledger era of the acquired state, Shelley or later, as the current-era query answered it.</param>
    public static IQueryReq GetCurrentProtocolParams(Era era) => CreateBlockQuery(new GlobalQuery(3), era);

    /// <summary>
    /// Wraps a query in the block query envelope for the given ledger era.
    /// </summary>
    /// <param name="query">The inner query to wrap.</param>
    /// <param name="era">The ledger era of the acquired state, Shelley or later, as the current-era query answered it; not
    /// the negotiated version and not a transaction's era.</param>
    /// <returns>A <see cref="IQueryReq"/> wrapped for that era's block query.</returns>
    public static IQueryReq CreateBlockQuery(IQueryReq query, Era era) => new BaseQuery(0, new BaseQuery(0, new BaseQuery((int)era, query)));

    /// <summary>
    /// Creates a query request to retrieve UTxOs at the specified addresses.
    /// </summary>
    /// <param name="addresses">The list of serialized address bytes to query UTxOs for.</param>
    /// <param name="era">The ledger era of the acquired state, Shelley or later, as the current-era query answered it.</param>
    /// <returns>A <see cref="IQueryReq"/> for retrieving UTxOs by address.</returns>
    public static IQueryReq GetUtxoByAddress(List<ReadOnlyMemory<byte>> addresses, Era era) => CreateBlockQuery(new UtxoByAddressQuery(6, [.. addresses]), era);

    /// <summary>
    /// Creates a query request to retrieve UTxOs for the specified transaction inputs.
    /// </summary>
    /// <param name="txIns">The list of transaction inputs to query UTxOs for.</param>
    /// <param name="era">The ledger era of the acquired state, Shelley or later, as the current-era query answered it.</param>
    /// <returns>A <see cref="IQueryReq"/> for retrieving UTxOs by transaction input.</returns>
    public static IQueryReq GetUtxoByTxIns(List<TransactionInput> txIns, Era era) => CreateBlockQuery(new UtxoByTxInQuery(15, [.. txIns]), era);
}


/// <summary>
/// Base type for all LocalStateQuery request structures, serialized as CBOR.
/// </summary>
[CborSerializable]
[CborUnion]
public partial interface IQueryReq : ICborType
{
    /// <summary>Reads a <see cref="IQueryReq"/> and reports the number of bytes consumed.</summary>
    static IQueryReq Read(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        QueryUnionReader.Read(data, out bytesConsumed);
}

/// <summary>
/// Represents a base query wrapper in the LocalStateQuery mini-protocol that nests an inner query with a numeric selector.
/// </summary>
/// <param name="Query">The numeric query selector or era index.</param>
/// <param name="InnerQuery">The nested inner query, or null for terminal queries.</param>
[CborSerializable]
[CborList]
public partial record BaseQuery(
    [CborOrder(0)] int Query,
    [CborOrder(1)] IQueryReq? InnerQuery
) : CborRecord, IQueryReq;

/// <summary>
/// Represents a UTxO-by-address query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index for UTxO-by-address queries.</param>
/// <param name="Addresses">The list of serialized address bytes to query.</param>
[CborSerializable]
[CborList]
public partial record UtxoByAddressQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] List<ReadOnlyMemory<byte>> Addresses
) : CborRecord, IQueryReq;

/// <summary>
/// Represents a UTxO-by-transaction-input query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index for UTxO-by-transaction-input queries.</param>
/// <param name="TxIns">The list of transaction inputs to query.</param>
[CborSerializable]
[CborList]
public partial record UtxoByTxInQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] List<TransactionInput> TxIns
) : CborRecord, IQueryReq;
