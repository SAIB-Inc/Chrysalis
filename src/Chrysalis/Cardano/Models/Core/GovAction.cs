using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(ParameterChangeAction),
])]
public record GovAction : ICbor;

public record ParameterChangeAction(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborNullable<GovActionId> GovActionId,
    [CborProperty(2)] ProtocolParamUpdate ProtocolParamUpdate, //@TODO
    [CborProperty(3)] CborNullable<CborBytes> PolicyHash
) : GovAction;