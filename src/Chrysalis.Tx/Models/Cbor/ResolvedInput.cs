using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Models.Cbor;
[CborSerializable]
[CborList]
public partial record ResolvedInput(
    [CborOrder(0)] TransactionInput Outref,
    [CborOrder(1)] TransactionOutput Output
) : CborBase;
