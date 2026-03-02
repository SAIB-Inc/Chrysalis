using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalTxMonitor mini-protocol for monitoring the local mempool.
/// Allows querying transactions, sizes, and measures from the Cardano node's mempool.
/// </summary>
public class LocalTxMonitor(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => false;

    /// <summary>
    /// Acquires a snapshot of the current mempool state for subsequent queries.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message from the Cardano node.</returns>
    public async Task<LocalTxMonitorMessage> AcquireAsync(CancellationToken cancellationToken)
    {
        Acquire acquire = LocalTxMonitorMessages.Acquire();
        await _channelBuffer.SendFullMessageAsync(acquire, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the currently acquired mempool snapshot, allowing a new snapshot to be acquired.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message from the Cardano node.</returns>
    public async Task<LocalTxMonitorMessage> ReleaseAsync(CancellationToken cancellationToken)
    {
        Release release = LocalTxMonitorMessages.Release();
        await _channelBuffer.SendFullMessageAsync(release, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the next transaction from the acquired mempool snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message containing the next transaction or an empty response if no more transactions.</returns>
    public async Task<LocalTxMonitorMessage> NextTxAsync(CancellationToken cancellationToken)
    {
        NextTx nextTx = LocalTxMonitorMessages.NextTx();
        await _channelBuffer.SendFullMessageAsync(nextTx, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a specific transaction exists in the mempool.
    /// </summary>
    /// <param name="txId">The transaction ID to check for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message indicating whether the transaction is present.</returns>
    // @TODO
    public async Task<LocalTxMonitorMessage> HasTxAsync(string txId, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(LocalTxMonitorMessages.HasTx(txId), cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the current mempool size information (number of transactions, total bytes, etc.).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message containing mempool size data.</returns>
    public async Task<LocalTxMonitorMessage> GetSizesAsync(CancellationToken cancellationToken)
    {
        GetSizes getSizes = LocalTxMonitorMessages.DefaultGetSizes;
        await _channelBuffer.SendFullMessageAsync(getSizes, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves mempool capacity measures from the Cardano node.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message containing mempool measures.</returns>
    // @TODO
    public async Task<LocalTxMonitorMessage> GetMeasuresAsync(CancellationToken cancellationToken)
    {
        GetMeasures getMeasures = LocalTxMonitorMessages.DefaultGetMeasures;
        await _channelBuffer.SendFullMessageAsync(getMeasures, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }
}
