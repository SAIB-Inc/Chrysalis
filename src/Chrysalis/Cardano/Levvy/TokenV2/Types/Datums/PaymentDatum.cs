using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Cardano.Levvy.TokenV2.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record PaymentDatum(OutputReference OutputReference) : CborBase;