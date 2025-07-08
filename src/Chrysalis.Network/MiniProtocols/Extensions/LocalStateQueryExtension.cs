using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;

namespace Chrysalis.Network.MiniProtocols.Extensions;

/// <summary>
/// Extension methods for the LocalStateQuery protocol
/// </summary>
public static class LocalStateQueryExtension
{
    private static readonly string DeserializationError = "Failed to deserialize response";
    private static readonly string InvalidResponseError = "Invalid response format";

    /// <summary>
    /// Executes a query with automatic acquire/release handling for fresh state
    /// </summary>
    private static async Task<T> ExecuteWithFreshStateAsync<T>(
        this LocalStateQuery localStateQuery,
        Func<Task<Result>> queryFunc,
        CancellationToken cancellationToken) where T : CborBase
    {
        // Release if already acquired, then acquire fresh state
        if (localStateQuery.IsAcquired)
        {
            await localStateQuery.ReleaseAsync(cancellationToken);
        }
        await localStateQuery.AcquireAsync(null, cancellationToken); // null = volatile tip
        
        try
        {
            Result queryResult = await queryFunc();
            byte[] rawBytes = ExtractRawBytes(queryResult);
            
            try
            {
                return CborSerializer.Deserialize<T>(rawBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{DeserializationError}: {ex.Message}", ex);
            }
        }
        finally
        {
            await localStateQuery.ReleaseAsync(cancellationToken);
        }
    }


    /// <summary>
    /// Gets the current chain tip. Always re-acquires state to ensure fresh data.
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task yielding the current tip</returns>
    /// <exception cref="InvalidOperationException">Thrown when response extraction or deserialization fails</exception>
    public static Task<Tip> GetTipAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        return localStateQuery.ExecuteWithFreshStateAsync<Tip>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetTip, cancellationToken),
            cancellationToken);
    }

    public static Task<CurrentEraQueryResponse> GetCurrentEraAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentEraQueryResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetCurrentEra, cancellationToken),
            cancellationToken);
    }

    public static Task<CurrentProtocolParamsResponse> GetCurrentProtocolParamsAsync(this LocalStateQuery localStateQuery, CancellationToken cancellationToken = default)
    {
        return localStateQuery.ExecuteWithFreshStateAsync<CurrentProtocolParamsResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetCurrentProtocolParams, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by address
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
    /// <param name="addresses">List of addresses to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task yielding the UTxO response</returns>
    /// <exception cref="InvalidOperationException">Thrown when response extraction or deserialization fails</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByAddressAsync(
        this LocalStateQuery localStateQuery,
        List<byte[]> addresses,
        CancellationToken cancellationToken = default)
    {
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetUtxoByAddress(addresses), cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Gets UTxOs by transaction inputs
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
    /// <param name="txIns">List of transaction inputs to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task yielding the UTxO response</returns>
    /// <exception cref="InvalidOperationException">Thrown when response extraction or deserialization fails</exception>
    public static Task<UtxoByAddressResponse> GetUtxosByTxInAsync(
        this LocalStateQuery localStateQuery,
        List<TransactionInput> txIns,
        CancellationToken cancellationToken = default)
    {
        return localStateQuery.ExecuteWithFreshStateAsync<UtxoByAddressResponse>(
            async () => await localStateQuery.QueryAsync(RawQueries.GetUtxoByTxIns(txIns), cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Extracts raw bytes from a query result
    /// </summary>
    /// <param name="result">The query result to extract bytes from</param>
    /// <returns>The raw bytes</returns>
    /// <exception cref="InvalidOperationException">Thrown when raw bytes extraction fails</exception>
    private static byte[] ExtractRawBytes(Result result)
    {
        try
        {
            if (result.QueryResult.Value == null)
                throw new InvalidOperationException($"{InvalidResponseError}: Result has no raw data");

            return [.. result.QueryResult.Value];
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"{InvalidResponseError}: {ex.Message}", ex);
        }
    }

}