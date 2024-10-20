using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record Credential( 
    [CborProperty(0)] CborInt CredentialType,
    [CborProperty(1)] CborBytes Hash
) : RawCbor;