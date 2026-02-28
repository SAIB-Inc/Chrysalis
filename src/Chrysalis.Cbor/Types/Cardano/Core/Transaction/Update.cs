using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// A protocol parameter update proposal containing the proposed changes and the target epoch.
/// </summary>
/// <param name="ProposedProtocolParameterUpdates">The proposed protocol parameter updates keyed by genesis delegate.</param>
/// <param name="Epoch">The epoch in which the update should take effect.</param>
[CborSerializable]
[CborList]
public partial record Update(
    [CborOrder(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborOrder(1)] ulong Epoch
) : CborBase;
