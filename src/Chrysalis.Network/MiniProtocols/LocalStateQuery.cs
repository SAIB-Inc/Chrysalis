// using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
// using Chrysalis.Network.Multiplexer;
// using Chrysalis.Cbor.Serialization;
// using Chrysalis.Network.Cbor.LocalStateQuery;
// using Chrysalis.Network.Cbor.Common;

// namespace Chrysalis.Network.MiniProtocols;

// /// <summary>
// /// Implementation of the Local State Query mini-protocol as described in the Ouroboros specification
// /// </summary>
// /// <remarks>
// /// Initializes a new instance of the LocalStateQuery mini-protocol.
// /// </remarks>
// /// <param name="channel">The AgentChannel to wrap for protocol communication.</param>
// public class LocalStateQuery(AgentChannel channel)
// {
//     private readonly ChannelBuffer _buffer = new(channel);

//     private static readonly Error AcquireFailed = Error.New("Acquire failed");

//     /// <summary>
//     /// Executes a query against the local state at the specified point.
//     /// </summary>
//     /// <param name="point">Optional point in chain history to query state at.</param>
//     /// <param name="query">The query to execute.</param>
//     /// <returns>An Aff monad yielding the query result.</returns>
//     public Aff<Result> Query(Option<Point> point, BlockQuery query) =>
//         from _ in _buffer.SendFullMessage(AcquireTypes.Default(point))
//         from acquireResponse in _buffer.RecieveFullMessage<LocalStateQueryMessage>()
//         from __ in guard(acquireResponse is Acquired, AcquireFailed)
//         from ___ in _buffer.SendFullMessage(QueryRequest.New(query))
//         from response in _buffer.RecieveFullMessage<LocalStateQueryMessage>()
//         from result in Aff(() => ValueTask.FromResult(
//             CborSerializer.Deserialize<Result>(CborSerializer.Serialize(response))
//         ))
//         select result;
// }