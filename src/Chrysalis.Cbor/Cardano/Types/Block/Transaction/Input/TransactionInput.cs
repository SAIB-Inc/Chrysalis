using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] byte[] TransactionId,
    [CborOrder(1)] ulong Index
) : CborBase;
