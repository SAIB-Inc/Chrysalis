using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Plutus.Types.Address;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Address(
    [CborIndex(0)] Credential PaymentCredential,
    [CborIndex(1)] Option<Inline<Credential>> StakeCredential
) : CborBase;