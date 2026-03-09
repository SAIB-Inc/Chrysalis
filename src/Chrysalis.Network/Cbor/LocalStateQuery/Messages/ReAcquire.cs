using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Base CBOR union type for ReAcquire messages in the Ouroboros LocalStateQuery mini-protocol, used to re-acquire the ledger state at a different point.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record ReAcquire : LocalStateQueryMessage;

/// <summary>
/// Factory methods for creating ReAcquire messages in the LocalStateQuery mini-protocol.
/// </summary>
public static class ReAcquireIdxs
{
    /// <summary>
    /// Creates a ReAcquire message for the specified point, or the volatile tip if no point is provided.
    /// </summary>
    /// <param name="point">The chain point to re-acquire, or null for the volatile tip.</param>
    /// <returns>A <see cref="ReAcquire"/> message for the specified target.</returns>
    public static ReAcquire Default(Point? point = null)
    {
        return point is not null ? SpecificPoint(point) : VolatileTip;
    }

    /// <summary>
    /// Creates a ReAcquire message to re-acquire the ledger state at a specific chain point.
    /// </summary>
    /// <param name="point">The chain point to re-acquire.</param>
    /// <returns>A <see cref="ReAcquireSpecificPoint"/> message.</returns>
    public static ReAcquireSpecificPoint SpecificPoint(Point point)
    {
        return new(6, point);
    }

    /// <summary>
    /// Gets a ReAcquire message to re-acquire the ledger state at the volatile (most recent) tip.
    /// </summary>
    public static ReAcquireVolatileTip VolatileTip => new(9);

    /// <summary>
    /// Gets a ReAcquire message to re-acquire the ledger state at the immutable tip.
    /// </summary>
    public static ReAcquireImmutableTip ImmutableTip => new(11);
}

/// <summary>
/// Represents a ReAcquire message targeting a specific chain point in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Point">The specific chain point to re-acquire.</param>
[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record ReAcquireSpecificPoint(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point
) : ReAcquire;

/// <summary>
/// Represents a ReAcquire message targeting the volatile (most recent) tip in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(9)]
public partial record ReAcquireVolatileTip(
    [CborOrder(0)] int Idx
) : ReAcquire;

/// <summary>
/// Represents a ReAcquire message targeting the immutable tip in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(11)]
public partial record ReAcquireImmutableTip(
    [CborOrder(0)] int Idx
) : ReAcquire;
