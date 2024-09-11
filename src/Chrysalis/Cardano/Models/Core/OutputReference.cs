using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Constr, Index = 0)]
public record OutputReference(
    [CborProperty(0)]
    TransactionId TransactionId,

    [CborProperty(1)]
    CborUlong Index
) : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record TransactionId(CborBytes Hash) : ICbor;