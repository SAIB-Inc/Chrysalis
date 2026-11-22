using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using SAIB.Cbor;

namespace Chrysalis.Network.MiniProtocols.Extensions;

/// <summary>
/// Extension methods for the LocalStateQuery protocol.
/// </summary>
public static class LocalStateQueryExtension
{
    private const string DeserializationError = "Failed to deserialize response";

    /// <summary>
    /// Executes a query with automatic acquire/release handling for fresh state.
    /// </summary>
    private static async Task<T> ExecuteWithFreshStateAsync<T>(
        this LocalStateQuery localStateQuery,
        Func<Task<Result>> queryFunc,
        bool eraSpecific,
        CancellationToken cancellationToken) where T : CborRecord
    {
        // Reject before any lifecycle message, including release of an existing snapshot.
        localStateQuery.EnsureBuiltInQueryVersion();
        // Release if already acquired, then acquire fresh state
        if (localStateQuery.IsAcquired)
        {
            await localStateQuery.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        await localStateQuery.AcquireAsync(null, cancellationToken).ConfigureAwait(false); // null = volatile tip

        try
        {
            return DecodeResult<T>(await queryFunc().ConfigureAwait(false), eraSpecific);
        }
        finally
        {
            await localStateQuery.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
    }


    /// <summary>
    /// Gets the current chain tip. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the ledger tip: its point, with <see cref="Tip.BlockNumber"/> null, because this query reports no block number.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in another era than the query, or the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<Tip> GetTipAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<Tip>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetTip, cancellationToken).ConfigureAwait(false),
            eraSpecific: true, cancellationToken);
    }

    /// <summary>
    /// Gets the current era of the Cardano blockchain. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the current era response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<CurrentEraQueryResponse> GetCurrentEraAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentEraQueryResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetCurrentEra, cancellationToken).ConfigureAwait(false),
            eraSpecific: false, cancellationToken);
    }

    /// <summary>
    /// Gets the current protocol parameters from the Cardano node. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the current protocol parameters.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in another era than the query, or the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<CurrentProtocolParamsResponse> GetCurrentProtocolParamsAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentProtocolParamsResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetCurrentProtocolParams, cancellationToken).ConfigureAwait(false),
            eraSpecific: true, cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by address.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="addresses">List of addresses to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the UTxO response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in another era than the query, or the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByAddressAsync(
        this LocalStateQuery localStateQuery,
        List<ReadOnlyMemory<byte>> addresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetUtxoByAddress(addresses), cancellationToken).ConfigureAwait(false),
            eraSpecific: true, cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by transaction inputs.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="txIns">List of transaction inputs to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the UTxO response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in another era than the query, or the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByTxInAsync(
        this LocalStateQuery localStateQuery,
        List<TransactionInput> txIns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetUtxoByTxIns(txIns), cancellationToken).ConfigureAwait(false),
            eraSpecific: true, cancellationToken);
    }

    /// <summary>Decodes a helper's result, or throws for an era mismatch or a result that cannot be decoded.</summary>
    private static T DecodeResult<T>(Result result, bool eraSpecific) where T : CborRecord
    {
        ReadOnlyMemory<byte> raw = result.QueryResult.Raw;
        try
        {
            // An era-specific query answers [value], or [queryEra, ledgerEra] when the ledger is in another era
            // (ouroboros-consensus 5.4.0.0, HardFork/Combinator/Serialisation/Common.hs:540-557).
            return eraSpecific && raw.Span is [0x82, ..]
                ? throw EraMismatch(raw)
                : CborSerializer.Deserialize<T>(raw);
        }
        catch (CborDecodeException error)
        {
            throw new InvalidOperationException($"{DeserializationError}: {error.Message}", error);
        }
    }

    private static InvalidOperationException EraMismatch(ReadOnlyMemory<byte> raw)
    {
        try
        {
            return new InvalidOperationException(CborText.EraMismatch(raw.Span, "query"));
        }
        catch (CborException error)
        {
            throw CborError.Wrap(error, raw, "Result.QueryResult", "era mismatch [[index, name], [index, name]]");
        }
    }
}
