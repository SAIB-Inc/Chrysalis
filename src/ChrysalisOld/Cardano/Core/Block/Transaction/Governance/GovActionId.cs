using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record GovActionId( 
    [CborProperty(0)] CborBytes TransactionId,
    [CborProperty(1)] CborInt GovActionIndex
) : RawCbor;