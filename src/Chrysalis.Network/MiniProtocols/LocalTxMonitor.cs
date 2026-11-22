using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalTxMonitor mini-protocol for monitoring the local mempool.
/// Allows querying transactions, sizes, and measures from the Cardano node's mempool.
/// </summary>
public class LocalTxMonitor : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer;
    private readonly N2CVersionContext? _versions;

    /// <summary>Initializes a standalone handler whose negotiated version remains the caller's responsibility.</summary>
    /// <param name="channel">The local-tx-monitor channel.</param>
    /// <remarks>For compatibility, operations behave as before when this legacy constructor has no negotiated context.</remarks>
    public LocalTxMonitor(AgentChannel channel) : this(channel, (N2CVersionContext?)null) { }

    /// <summary>Initializes a standalone handler bound to its caller's negotiated N2C version.</summary>
    /// <param name="channel">The local-tx-monitor channel.</param>
    /// <param name="negotiatedVersion">The real negotiated N2C wire version.</param>
    public LocalTxMonitor(AgentChannel channel, N2CVersion negotiatedVersion) : this(channel, new N2CVersionContext(negotiatedVersion)) { }

    internal LocalTxMonitor(AgentChannel channel, N2CVersionContext? versions)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channelBuffer = new ChannelBuffer(channel);
        _versions = versions;
    }

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
    /// Releases the acquired mempool snapshot. The node sends no reply, so this returns once the release is sent;
    /// a new snapshot can then be acquired on the same connection.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the release is sent.</returns>
    /// <exception cref="OperationCanceledException">The token is already cancelled; nothing is sent.</exception>
    public Task ReleaseAsync(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : _channelBuffer.SendFullMessageAsync(LocalTxMonitorMessages.Release(), cancellationToken);

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
    /// Checks whether a transaction is in the acquired mempool snapshot.
    /// </summary>
    /// <param name="txId">The transaction id as 64 hexadecimal digits, sent with the Conway era.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The node's <see cref="ReplyHasTx"/>.</returns>
    /// <exception cref="ArgumentException">Thrown before anything is sent when <paramref name="txId"/> is not 64 hexadecimal digits.</exception>
    public Task<LocalTxMonitorMessage> HasTxAsync(string txId, CancellationToken cancellationToken) =>
        HasTxAsync(LocalTxMonitorMessages.ConwayTxId(txId), cancellationToken);

    /// <summary>
    /// Checks whether a transaction is in the acquired mempool snapshot.
    /// </summary>
    /// <param name="txId">The era-tagged transaction id.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The node's <see cref="ReplyHasTx"/>.</returns>
    /// <exception cref="ArgumentException">Thrown before anything is sent when the id is not 32 bytes.</exception>
    public async Task<LocalTxMonitorMessage> HasTxAsync(EraTxId txId, CancellationToken cancellationToken)
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
    /// Retrieves mempool capacity measures from the Cardano node (requires N2C V20 when context is available).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response message containing mempool measures.</returns>
    public async Task<LocalTxMonitorMessage> GetMeasuresAsync(CancellationToken cancellationToken)
    {
        _versions?.EnsureMinimum(32788, nameof(GetMeasuresAsync));
        GetMeasures getMeasures = LocalTxMonitorMessages.DefaultGetMeasures;
        await _channelBuffer.SendFullMessageAsync(getMeasures, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }
}
