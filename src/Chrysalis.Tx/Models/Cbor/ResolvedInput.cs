using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Models.Cbor;

/// <summary>
/// Represents a resolved UTxO consisting of a transaction input reference and its corresponding output.
/// </summary>
/// <param name="Outref">The transaction input reference (tx hash + index).</param>
/// <param name="Output">The transaction output at this reference.</param>
[CborSerializable]
[CborList]
public partial record ResolvedInput(
    [CborOrder(0)] TransactionInput Outref,
    [CborOrder(1)] TransactionOutput Output
) : CborBase;
