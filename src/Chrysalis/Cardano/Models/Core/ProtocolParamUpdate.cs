using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record ProtocolParamUpdate( //@TODO  
    [CborProperty(0)] CborUlong Value
) : ICbor;