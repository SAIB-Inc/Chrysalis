using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.MiniProtocols;

public class LocalStateQuery(AgentChannel channel) : IAsyncDisposable
{
    private readonly ChannelBuffer _buffer = new(channel);

    public bool IsAcquired { get; private set; }

    public async Task<Result> QueryAsync(QueryReq query, CancellationToken cancellationToken)
    {
        if (!IsAcquired)
        {
            throw new InvalidOperationException("Must acquire state before querying. Call AcquireAsync first.");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken).ConfigureAwait(false);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken).ConfigureAwait(false);
    }

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

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (IsAcquired)
        {
            await _buffer.SendFullMessageAsync(new Release(5), cancellationToken).ConfigureAwait(false);
            IsAcquired = false;
        }
    }


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
