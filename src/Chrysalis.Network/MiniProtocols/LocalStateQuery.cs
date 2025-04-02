using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.MiniProtocols;

public class LocalStateQuery(AgentChannel channel)
{
    private readonly ChannelBuffer _buffer = new(channel);

    // States
    private bool IsAcquired = false;

    public async Task<Result> QueryAsync(Point? point, QueryReq query, CancellationToken cancellationToken)
    {
        if (!IsAcquired)
        {
            await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken);
            LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken);

            if (acquireResponse is not Acquired)
            {
                throw new Exception("Failed to acquire");
            }

            IsAcquired = true;
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken);
    }
}