using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record Credential(
    [CborProperty(0)] CborInt CredentialType, 
    [CborProperty(1)] CborBytes Hash
) : ICbor;