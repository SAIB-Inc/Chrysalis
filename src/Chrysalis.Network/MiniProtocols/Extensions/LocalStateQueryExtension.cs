using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
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
    /// Acquires fresh state at the volatile tip and sends <paramref name="query"/>. With <paramref name="eraQuery"/> null it
    /// returns that answer; otherwise the answer is the ledger era, and it sends the query built for that era. Then releases.
    /// </summary>
    private static async Task<T> ExecuteWithFreshStateAsync<T>(
        this LocalStateQuery localStateQuery,
        IQueryReq query,
        Func<Era, IQueryReq>? eraQuery,
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
            Result answer = await localStateQuery.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            return eraQuery is null
                ? DecodeResult<T>(answer, eraSpecific: false)
                : DecodeResult<T>(await localStateQuery.QueryAsync(eraQuery(LedgerEra(answer)), cancellationToken).ConfigureAwait(false), eraSpecific: true);
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
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in Byron or in an era this version does not know, when the node answers in another era than it reported, or when a result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<Tip> GetTipAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<Tip>(RawQueries.GetCurrentEra, RawQueries.GetTip, cancellationToken);
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
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentEraQueryResponse>(RawQueries.GetCurrentEra, null, cancellationToken);
    }

    /// <summary>
    /// Gets the system start, the UTC time of slot zero, in any era. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the system start.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<SystemStartResponse> GetSystemStartAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<SystemStartResponse>(RawQueries.GetSystemStart, null, cancellationToken);
    }

    /// <summary>
    /// Gets the era history, one summary per era the ledger knows, in any era. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the era history.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<EraHistoryResponse> GetEraHistoryAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<EraHistoryResponse>(RawQueries.GetEraHistory, null, cancellationToken);
    }

    /// <summary>
    /// Gets the current protocol parameters from the Cardano node. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the current protocol parameters.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in Byron or in an era this version does not know, when the node answers in another era than it reported, or when a result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<CurrentProtocolParamsResponse> GetCurrentProtocolParamsAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentProtocolParamsResponse>(RawQueries.GetCurrentEra, RawQueries.GetCurrentProtocolParams, cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by address.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="addresses">List of addresses to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the UTxO response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in Byron or in an era this version does not know, when the node answers in another era than it reported, or when a result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByAddressAsync(
        this LocalStateQuery localStateQuery,
        List<ReadOnlyMemory<byte>> addresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(RawQueries.GetCurrentEra, era => RawQueries.GetUtxoByAddress(addresses, era), cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by transaction inputs.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance.</param>
    /// <param name="txIns">List of transaction inputs to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task yielding the UTxO response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the node's ledger is in Byron or in an era this version does not know, when the node answers in another era than it reported, or when a result cannot be decoded.</exception>
    /// <exception cref="CborDecodeException">Thrown when the node's reply is not a well-formed result message.</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByTxInAsync(
        this LocalStateQuery localStateQuery,
        List<TransactionInput> txIns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localStateQuery);
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(RawQueries.GetCurrentEra, era => RawQueries.GetUtxoByTxIns(txIns, era), cancellationToken);
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

    // Byron reads a query as a one-byte tag (ouroboros-consensus 5.4.0.0, Byron/Ledger/Ledger.hs:556-565), and an era
    // this version does not know is refused rather than queried with a known era's types.
    private static Era LedgerEra(Result result)
    {
        ulong index = DecodeResult<CurrentEraQueryResponse>(result, eraSpecific: false).CurrentEra;
        return index > (ulong)Era.Byron && index <= (ulong)Enum.GetValues<Era>().Max()
            ? (Era)index
            : throw new InvalidOperationException($"The node's ledger is in era {index}, which these helpers cannot query.");
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
