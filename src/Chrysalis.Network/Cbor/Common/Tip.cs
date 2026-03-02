using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Common;

/// <summary>
/// Represents the current tip of the Cardano blockchain as reported by a node, containing a point and block number.
/// </summary>
/// <param name="Slot">The point (slot and block hash) at the tip of the chain.</param>
/// <param name="BlockNumber">The block number at the tip of the chain, or null if at origin.</param>
[CborSerializable]
[CborList]
public partial record Tip(
    [CborOrder(0)] Point Slot,
    [CborOrder(1)] int? BlockNumber
) : CborBase;
