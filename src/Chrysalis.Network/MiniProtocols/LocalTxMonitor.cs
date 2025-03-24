using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class LocalTxMonitor(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);
    public async Task<LocalTxMonitorMessage> AcquireAsync(CancellationToken cancellationToken)
    {
        Acquire acquire = LocalTxMonitorMessages.Acquire();
        await _channelBuffer.SendFullMessageAsync(acquire, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }

    public async Task<LocalTxMonitorMessage> ReleaseAsync(CancellationToken cancellationToken)
    {
        Release release = LocalTxMonitorMessages.Release();
        await _channelBuffer.SendFullMessageAsync(release, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }

    public async Task<LocalTxMonitorMessage> NextTxAsync(CancellationToken cancellationToken)
    {
        NextTx nextTx = LocalTxMonitorMessages.NextTx();
        await _channelBuffer.SendFullMessageAsync(nextTx, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }

    // @TODO
    public async Task<LocalTxMonitorMessage> HasTxAsync(string txId, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(LocalTxMonitorMessages.HasTx(txId), cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }

    public async Task<LocalTxMonitorMessage> GetSizesAsync(CancellationToken cancellationToken)
    {
        GetSizes getSizes = LocalTxMonitorMessages.GetSizes();
        await _channelBuffer.SendFullMessageAsync(getSizes, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }

    // @TODO
    public async Task<LocalTxMonitorMessage> GetMeasuresAsync(CancellationToken cancellationToken)
    {

        GetMeasures getMeasures = LocalTxMonitorMessages.GetMeasures();
        await _channelBuffer.SendFullMessageAsync(getMeasures, cancellationToken);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxMonitorMessage>(cancellationToken);
    }
}

