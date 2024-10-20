using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

[CborSerializable(CborType.List)]
public record GovActionId( 
    [CborProperty(0)] CborBytes TransactionId,
    [CborProperty(1)] CborInt GovActionIndex
) : RawCbor;