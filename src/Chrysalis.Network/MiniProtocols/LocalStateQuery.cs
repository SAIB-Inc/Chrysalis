using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using System;
using System.Threading.Tasks;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Query = Chrysalis.Network.Cbor.LocalStateQuery.Messages.Query;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Local State Query mini-protocol as described in the Ouroboros specification
/// </summary>
public class LocalStateQuery(AgentChannel channel)
{

    public Aff<Result> QueryTip() =>
        from acquireChunk in channel.EnqueueChunk(
            CborSerializer.Serialize(AcquireTypes.VolatileTip))
        from acquireResponseChunk in channel.ReceiveNextChunk()
        let acquireResponse = CborSerializer.Deserialize<LocalStateQueryMessage>(acquireResponseChunk)
        from _ in acquireResponse switch
        {
            Acquired => unitAff,
            Failure f => FailAff<Unit>(new Exception($"Acquisition failed: {f.Reason}")),
            _ => FailAff<Unit>(new Exception("Unexpected acquire response"))
        }
        from queryChunk in channel.EnqueueChunk(
            QueryRequest.New(CborSerializer.Serialize(CborSerializer.Serialize(Queries.GetLedgerTipQuery))
        from responseChunk in channel.ReceiveNextChunk()
        let response = CborSerializer.Deserialize<Result<Tip>>(responseChunk)
        select response.Value;
}