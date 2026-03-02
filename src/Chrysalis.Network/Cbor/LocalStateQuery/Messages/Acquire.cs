using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Base CBOR union type for Acquire messages in the Ouroboros LocalStateQuery mini-protocol, used to acquire the ledger state at a specific point.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Acquire : LocalStateQueryMessage;

/// <summary>
/// Factory methods for creating Acquire messages in the LocalStateQuery mini-protocol.
/// </summary>
public static class AcquireTypes
{
    /// <summary>
    /// Creates an Acquire message for the specified point, or the volatile tip if no point is provided.
    /// </summary>
    /// <param name="point">The chain point to acquire, or null for the volatile tip.</param>
    /// <returns>An <see cref="Acquire"/> message for the specified target.</returns>
    public static Acquire Default(Point? point)
    {
        return point is not null ? SpecificPoint(point) : VolatileTip;
    }

    /// <summary>
    /// Creates an Acquire message to acquire the ledger state at a specific chain point.
    /// </summary>
    /// <param name="point">The chain point to acquire.</param>
    /// <returns>A <see cref="Messages.SpecificPoint"/> message.</returns>
    public static Acquire SpecificPoint(Point point)
    {
        return new SpecificPoint(0, point);
    }

    /// <summary>
    /// Gets an Acquire message to acquire the ledger state at the volatile (most recent) tip.
    /// </summary>
    public static Acquire VolatileTip => new VolatileTip(8);

    /// <summary>
    /// Gets an Acquire message to acquire the ledger state at the immutable tip.
    /// </summary>
    public static Acquire ImmutableTip => new ImmutableTip(10);
}

/// <summary>
/// Represents an Acquire message targeting a specific chain point in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Point">The specific chain point to acquire.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record SpecificPoint(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point Point
) : Acquire;

/// <summary>
/// Represents an Acquire message targeting the volatile (most recent) tip in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(8)]
public partial record VolatileTip(
    [CborOrder(0)] int Idx
) : Acquire;

/// <summary>
/// Represents an Acquire message targeting the immutable tip in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(10)]
public partial record ImmutableTip(
    [CborOrder(0)] int Idx
) : Acquire;
