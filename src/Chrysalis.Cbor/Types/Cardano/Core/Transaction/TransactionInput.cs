using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// A reference to an unspent transaction output (UTxO) identified by its transaction hash and output index.
/// </summary>
/// <param name="TransactionId">The hash of the transaction containing the output.</param>
/// <param name="Index">The index of the output within the transaction.</param>
[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] ReadOnlyMemory<byte> TransactionId,
    [CborOrder(1)] ulong Index
) : CborBase;
