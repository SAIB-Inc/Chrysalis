using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Plutus;

[CborSerializable(CborType.Constr, Index = 0)]
public record OutputReference(
    [CborProperty(0)]
    TransactionId TransactionId,

    [CborProperty(1)]
    CborUlong Index
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record TransactionId(CborBytes Hash) : RawCbor;