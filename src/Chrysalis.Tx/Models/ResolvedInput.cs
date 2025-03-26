

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Models;
[CborSerializable]
[CborList]
public partial record ResolvedInput(
    [CborOrder(0)] TransactionInput Outref,
    [CborOrder(1)] TransactionOutput Output
) : CborBase;
