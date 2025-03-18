using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

// [CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborIndex(0)] byte[] TransactionId,
    [CborIndex(1)] ulong Index
) : CborBase<TransactionInput>;
