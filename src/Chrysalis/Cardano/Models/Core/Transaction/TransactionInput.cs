using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.List)]
public record TransactionInput(
    [CborProperty(0)] CborBytes TransactionId,
    [CborProperty(1)] CborInt Index
) : RawCbor;