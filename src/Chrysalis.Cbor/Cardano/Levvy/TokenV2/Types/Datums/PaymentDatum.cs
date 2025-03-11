using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Plutus.Types;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Levvy.TokenV2.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record PaymentDatum(OutputReference OutputReference) : CborBase;