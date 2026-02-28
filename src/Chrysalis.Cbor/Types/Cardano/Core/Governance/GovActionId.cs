using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Identifies a governance action by its originating transaction and index within that transaction.
/// </summary>
/// <param name="TransactionId">The hash of the transaction that proposed the governance action.</param>
/// <param name="GovActionIndex">The index of the governance action within the transaction.</param>
[CborSerializable]
[CborList]
public partial record GovActionId(
    [CborOrder(0)] ReadOnlyMemory<byte> TransactionId,
    [CborOrder(1)] int GovActionIndex
) : CborBase;
