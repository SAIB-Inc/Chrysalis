using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Plutus.Address;

/// <summary>
/// A Plutus address consisting of a payment credential and an optional stake credential.
/// </summary>
/// <param name="PaymentCredential">The payment credential identifying the owner.</param>
/// <param name="StakeCredential">The optional stake credential for delegation.</param>
[CborSerializable]
[CborConstr(0)]
public partial record Address(
    [CborOrder(0)] ICredential PaymentCredential,
    [CborOrder(1)] ICborOption<Inline<ICredential>> StakeCredential
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
