using Chrysalis.Network.Cbor.LocalTxMonitor;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class LocalTxMonitor(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => false;

    public async Task<LocalTxMonitorMessage> AcquireAsync(CancellationToken cancellationToken)
    {
        Acquire acquire = LocalTxMonitorMessages.Acquire();
        await _channelBuffer.SendFullMessageAsync(acquire, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalTxMonitorMessage> ReleaseAsync(CancellationToken cancellationToken)
    {
        Release release = LocalTxMonitorMessages.Release();
        await _channelBuffer.SendFullMessageAsync(release, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalTxMonitorMessage> NextTxAsync(CancellationToken cancellationToken)
    {
        NextTx nextTx = LocalTxMonitorMessages.NextTx();
        await _channelBuffer.SendFullMessageAsync(nextTx, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    // @TODO
    public async Task<LocalTxMonitorMessage> HasTxAsync(string txId, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(LocalTxMonitorMessages.HasTx(txId), cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalTxMonitorMessage> GetSizesAsync(CancellationToken cancellationToken)
    {
        GetSizes getSizes = LocalTxMonitorMessages.DefaultGetSizes;
        await _channelBuffer.SendFullMessageAsync(getSizes, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }

    // @TODO
    public async Task<LocalTxMonitorMessage> GetMeasuresAsync(CancellationToken cancellationToken)
    {
        GetMeasures getMeasures = LocalTxMonitorMessages.DefaultGetMeasures;
        await _channelBuffer.SendFullMessageAsync(getMeasures, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken).ConfigureAwait(false);
    }
}
