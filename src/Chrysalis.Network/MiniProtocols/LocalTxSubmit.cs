using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public class LocalTxSubmit(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => false;

    public async Task<LocalTxSubmissionMessage> SubmitTxAsync(SubmitTx submit, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(submit, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxSubmissionMessage>(cancellationToken).ConfigureAwait(false);
    }
}
