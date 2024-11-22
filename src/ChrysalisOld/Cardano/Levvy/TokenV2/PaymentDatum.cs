using Chrysalis.Cardano.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Levvy;

[CborSerializable(CborType.Constr, Index = 0)]
public record PaymentDatum(OutputReference OutputReference) : RawCbor;