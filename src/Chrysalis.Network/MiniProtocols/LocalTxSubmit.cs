using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalTxSubmit mini-protocol for submitting transactions to the Cardano node.
/// </summary>
public class LocalTxSubmit(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _channelBuffer = new(channel);

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone => false;

    /// <summary>
    /// Submits a transaction to the Cardano node's mempool and waits for the acceptance or rejection response.
    /// </summary>
    /// <param name="submit">The transaction submission message containing the serialized transaction.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The submission response indicating acceptance or rejection with reason.</returns>
    public async Task<LocalTxSubmissionMessage> SubmitTxAsync(SubmitTx submit, CancellationToken cancellationToken)
    {
        await _channelBuffer.SendFullMessageAsync(submit, cancellationToken).ConfigureAwait(false);
        return await _channelBuffer.ReceiveFullMessageAsync<LocalTxSubmissionMessage>(cancellationToken).ConfigureAwait(false);
    }
}
