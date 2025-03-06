using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
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
    /// Gets UTxOs by address
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
    /// <param name="addresses">List of addresses to query</param>
    /// <returns>A task yielding the UTxO response</returns>
    /// <exception cref="InvalidOperationException">Thrown when response extraction or deserialization fails</exception>
    public static async Task<UtxoByAddressResponse> GetUtxosByAddressAsync(
        this LocalStateQuery localStateQuery,
        List<byte[]> addresses)
    {
        Result queryResult = await localStateQuery.QueryAsync(null, RawQueries.GetUtxoByAddress(addresses),
            default);
        byte[] rawBytes = await ExtractRawBytesAsync(queryResult);
        return await DeserializeResponseAsync<UtxoByAddressResponse>(rawBytes);
    }

    /// <summary>
    /// Gets UTxOs by transaction inputs
    /// </summary>
    /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
    /// <param name="txIns">List of transaction inputs to query</param>
    /// <returns>A task yielding the UTxO response</returns>
    /// <exception cref="InvalidOperationException">Thrown when response extraction or deserialization fails</exception>
    public static async Task<UtxoByAddressResponse> GetUtxosByTxInAsync(
        this LocalStateQuery localStateQuery,
        List<TransactionInput> txIns)
    {
        Result queryResult = await localStateQuery.QueryAsync(null, RawQueries.GetUtxoByTxIns(txIns),
            default);
        byte[] rawBytes = await ExtractRawBytesAsync(queryResult);
        return await DeserializeResponseAsync<UtxoByAddressResponse>(rawBytes);
    }

    /// <summary>
    /// Extracts raw bytes from a query result
    /// </summary>
    /// <param name="result">The query result to extract bytes from</param>
    /// <returns>A task yielding the raw bytes</returns>
    /// <exception cref="InvalidOperationException">Thrown when raw bytes extraction fails</exception>
    private static Task<byte[]> ExtractRawBytesAsync(Result result)
    {
        try
        {
            if (result.QueryResult.Raw == null)
                throw new InvalidOperationException($"{InvalidResponseError}: Result has no raw data");

            return Task.FromResult(result.QueryResult.Raw.Value.ToArray());
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"{InvalidResponseError}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserializes raw bytes to the specified type
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <param name="rawBytes">The raw bytes to deserialize</param>
    /// <returns>A task yielding the deserialized object</returns>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails</exception>
    private static Task<T> DeserializeResponseAsync<T>(byte[] rawBytes) where T : CborBase
    {
        try
        {
            return Task.FromResult(CborSerializer.Deserialize<T>(rawBytes));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{DeserializationError}: {ex.Message}", ex);
        }
    }
}