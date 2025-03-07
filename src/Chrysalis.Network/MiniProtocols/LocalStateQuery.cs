using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalStateQuery mini-protocol.
/// </summary>
/// <remarks>
/// Initializes a new instance of the LocalStateQuery protocol.
/// </remarks>
/// <param name="channel">The channel for protocol communication.</param>
public class LocalStateQuery(AgentChannel channel) : IMiniProtocol
{
    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    public static ProtocolType ProtocolType => ProtocolType.LocalStateQuery;

    private readonly ChannelBuffer _buffer = new(channel);

    /// <summary>
    /// Performs a state query at the specified chain point.
    /// </summary>
    /// <typeparam name="Result">The query result.</typeparam>
    /// <param name="point">Optional chain point to query at.</param>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when acquisition fails.</exception>
    public async Task<Result> QueryAsync<TResult>(Point? point, BlockQuery query, CancellationToken cancellationToken = default)
    {
        await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken);
        LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken);

        if (acquireResponse is not Acquired)
        {
            throw new InvalidOperationException($"Failed to acquire state at point {point}");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken);
    }
}