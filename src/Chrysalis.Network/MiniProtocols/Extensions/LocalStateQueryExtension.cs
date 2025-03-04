// using System;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using LanguageExt;
// using LanguageExt.Common;
// using static LanguageExt.Prelude;
// using Chrysalis.Cbor.Serialization;
// using Chrysalis.Network.Cbor.LocalStateQuery;
// using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
// using Chrysalis.Cbor.Types.Primitives;
// using Chrysalis.Cbor.Types;

// namespace Chrysalis.Network.MiniProtocols.Extensions;

// /// <summary>
// /// Extension methods for the LocalStateQuery protocol
// /// </summary>
// public static class LocalStateQueryExtension
// {
//     private static readonly Error DeserializationError = Error.New("Failed to deserialize response");
//     private static readonly Error InvalidResponseError = Error.New("Invalid response format");

//     /// <summary>
//     /// Gets UTxOs by address
//     /// </summary>
//     /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
//     /// <param name="addresses">List of addresses to query</param>
//     /// <returns>An Aff monad yielding the UTxO response</returns>
//     public static Aff<UtxoByAddressResponse> GetUtxosByAddress(this LocalStateQuery localStateQuery, List<byte[]> addresses) =>
//         from queryResult in localStateQuery.Query(None, RawQueries.GetUtxoByAddress(addresses))
//         from raw in ExtractRawBytes(queryResult)
//         from utxoResponse in DeserializeResponse<UtxoByAddressResponse>(raw)
//         select utxoResponse;

//     /// <summary>
//     /// Gets UTxOs by address
//     /// </summary>
//     /// <param name="localStateQuery">The LocalStateQuery protocol instance</param>
//     /// <param name="addresses">List of addresses to query</param>
//     /// <returns>An Aff monad yielding the UTxO response</returns>
//     public static Aff<UtxoByAddressResponse> GetUtxosByTxIn(this LocalStateQuery localStateQuery, List<TransactionInput> txIns) =>
//         from queryResult in localStateQuery.Query(None, RawQueries.GetUtxoByTxIns(txIns))
//         from raw in ExtractRawBytes(queryResult)
//         from utxoResponse in DeserializeResponse<UtxoByAddressResponse>(raw)
//         select utxoResponse;

//     /// <summary>
//     /// Extracts raw bytes from a query result
//     /// </summary>
//     /// <param name="result">The query result to extract bytes from</param>
//     /// <returns>An Aff monad yielding the raw bytes</returns>
//     private static Aff<byte[]> ExtractRawBytes(Result result) =>
//         Aff(() => ValueTask.FromResult(
//                 Try(() => result.QueryResult.Raw!.Value.ToArray())
//                     .Match(
//                         Succ: val => val,
//                         Fail: ex => throw new Exception($"{InvalidResponseError}: {ex.Message}")
//                     )
//             )
//         );

//     /// <summary>
//     /// Deserializes raw bytes to the specified type
//     /// </summary>
//     /// <typeparam name="T">The type to deserialize to</typeparam>
//     /// <param name="rawBytes">The raw bytes to deserialize</param>
//     /// <returns>An Aff monad yielding the deserialized object</returns>
//     private static Aff<T> DeserializeResponse<T>(byte[] rawBytes) where T : CborBase =>
//         Aff(() => ValueTask.FromResult(
//                 Try(() => CborSerializer.Deserialize<T>(rawBytes))
//                     .Match(
//                         Succ: val => val,
//                         Fail: ex => throw new Exception($"{DeserializationError}: {ex.Message}")
//                     )
//             )
//         );
// }
