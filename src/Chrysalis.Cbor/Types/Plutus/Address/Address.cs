using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

[CborSerializable]
[CborConstr(0)]
public partial record Address(
    [CborOrder(0)] Credential PaymentCredential,
    [CborOrder(1)] Option<Inline<Credential>> StakeCredential
) : CborBase;