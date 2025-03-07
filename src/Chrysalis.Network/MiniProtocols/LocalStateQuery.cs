using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.MiniProtocols;

public class LocalStateQuery(AgentChannel channel)
{
    private readonly ChannelBuffer _buffer = new(channel);

    public async Task<Result> QueryAsync(Point? point, BlockQuery query, CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken);
        LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken);

        if (acquireResponse is not Acquired)
        {
            throw new Exception("Failed to acquire");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken);
    }
}