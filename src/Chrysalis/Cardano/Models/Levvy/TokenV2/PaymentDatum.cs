using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Levvy.TokenV2;

[CborSerializable(CborType.Constr, Index = 0)]
public record PaymentDatum(OutputReference OutputReference) : ICbor;