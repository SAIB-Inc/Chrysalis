using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] byte[] TransactionId,
    [CborOrder(1)] ulong Index
) : CborBase;