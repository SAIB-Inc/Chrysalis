using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Plutus;

/// <summary>
/// A Plutus-style output reference identifying a specific UTxO by its transaction ID and output index.
/// </summary>
/// <param name="TransactionId">The transaction identifier containing the referenced output.</param>
/// <param name="Index">The index of the output within the transaction.</param>
[CborSerializable]
[CborConstr(0)]
public partial record OutputReference(
    [CborOrder(0)] TransactionId TransactionId,
    [CborOrder(1)] ulong Index
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

/// <summary>
/// A Plutus-style transaction identifier wrapping the transaction hash bytes.
/// </summary>
/// <param name="Hash">The transaction hash bytes.</param>
[CborSerializable]
[CborConstr(0)]
public partial record TransactionId(ReadOnlyMemory<byte> Hash) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
