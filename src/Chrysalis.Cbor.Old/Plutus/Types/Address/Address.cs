using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Functional;

namespace Chrysalis.Plutus.Types.Address;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Address(
    [CborProperty(0)] Credential PaymentCredential,
    [CborProperty(1)] Option<Inline<Credential>> StakeCredential
) : CborBase;