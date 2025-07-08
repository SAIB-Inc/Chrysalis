using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.MiniProtocols;

public class LocalStateQuery(AgentChannel channel) : IAsyncDisposable
{
    private readonly ChannelBuffer _buffer = new(channel);

    // State management
    private bool _isAcquired = false;

    public bool IsAcquired => _isAcquired;

    public async Task<Result> QueryAsync(QueryReq query, CancellationToken cancellationToken)
    {
        if (!_isAcquired)
        {
            throw new InvalidOperationException("Must acquire state before querying. Call AcquireAsync first.");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken);
    }

    public async Task AcquireAsync(Point? point, CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken);
        LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken);

        if (acquireResponse is not Acquired)
        {
            throw new Exception($"Failed to acquire state: {acquireResponse}");
        }

        _isAcquired = true;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (_isAcquired)
        {
            await _buffer.SendFullMessageAsync(new Release(new Value5(5)), cancellationToken);
            _isAcquired = false;
        }
    }


    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseAsync(CancellationToken.None);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}