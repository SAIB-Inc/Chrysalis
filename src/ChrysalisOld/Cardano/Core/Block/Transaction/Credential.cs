using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Credential( 
    [CborProperty(0)] CborInt CredentialType,
    [CborProperty(1)] CborBytes Hash
) : RawCbor;