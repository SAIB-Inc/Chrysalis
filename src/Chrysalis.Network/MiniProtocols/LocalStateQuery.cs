using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalStateQuery mini-protocol for querying the Cardano node's ledger state.
/// Supports acquire/release semantics to pin a consistent ledger snapshot before querying.
/// </summary>
public class LocalStateQuery(AgentChannel channel) : IAsyncDisposable
{
    private readonly ChannelBuffer _buffer = new(channel);

    /// <summary>Gets whether a ledger state snapshot has been acquired.</summary>
    public bool IsAcquired { get; private set; }

    /// <summary>
    /// Sends a query against the currently acquired ledger state and returns the result.
    /// </summary>
    /// <param name="query">The query request to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query result from the Cardano node.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no state has been acquired.</exception>
    public async Task<Result> QueryAsync(QueryReq query, CancellationToken cancellationToken)
    {
        if (!IsAcquired)
        {
            throw new InvalidOperationException("Must acquire state before querying. Call AcquireAsync first.");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken).ConfigureAwait(false);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires a ledger state snapshot at the specified point, or at the volatile tip if null.
    /// </summary>
    /// <param name="point">The chain point to acquire state at, or null for the volatile tip.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the node fails to acquire state at the requested point.</exception>
    public async Task AcquireAsync(Point? point, CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken).ConfigureAwait(false);
        LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken).ConfigureAwait(false);

        if (acquireResponse is not Acquired)
        {
            throw new InvalidOperationException($"Failed to acquire state: {acquireResponse}");
        }

        IsAcquired = true;
    }

    /// <summary>
    /// Releases the currently acquired ledger state snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (IsAcquired)
        {
            await _buffer.SendFullMessageAsync(new Release(5), cancellationToken).ConfigureAwait(false);
            IsAcquired = false;
        }
    }


    /// <summary>
    /// Releases the acquired state if held, performing best-effort cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Best effort cleanup - channel may already be disposed
        }
        catch (InvalidOperationException)
        {
            // Best effort cleanup - protocol state may be invalid
        }

        GC.SuppressFinalize(this);
    }
}
