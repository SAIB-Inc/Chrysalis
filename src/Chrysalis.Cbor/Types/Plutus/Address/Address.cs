using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

/// <summary>
/// A Plutus address consisting of a payment credential and an optional stake credential.
/// </summary>
/// <param name="PaymentCredential">The payment credential identifying the owner.</param>
/// <param name="StakeCredential">The optional stake credential for delegation.</param>
[CborSerializable]
[CborConstr(0)]
public partial record Address(
    [CborOrder(0)] Credential PaymentCredential,
    [CborOrder(1)] CborOption<Inline<Credential>> StakeCredential
) : CborBase;
