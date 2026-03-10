using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Common;

/// <summary>
/// Union type representing a point on the Cardano blockchain, either the origin or a specific slot and block hash.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Point : CborRecord
{
    /// <summary>
    /// Gets a <see cref="OriginPoint"/> representing the genesis (origin) of the blockchain.
    /// </summary>
    public static Point Origin => new OriginPoint();

    /// <summary>
    /// Creates a <see cref="SpecificPoint"/> at the given slot and block header hash.
    /// </summary>
    /// <param name="slot">The slot number on the blockchain.</param>
    /// <param name="hash">The block header hash identifying the block at the given slot.</param>
    /// <returns>A new <see cref="SpecificPoint"/> for the specified location.</returns>
    public static Point Specific(ulong slot, ReadOnlyMemory<byte> hash)
    {
        return new SpecificPoint(slot, hash);
    }
}

/// <summary>
/// Represents the origin (genesis) point of the Cardano blockchain. Encoded as an empty CBOR list.
/// </summary>
[CborSerializable]
[CborList]
public partial record OriginPoint() : Point;

/// <summary>
/// Represents a specific point on the Cardano blockchain identified by a slot number and block header hash.
/// </summary>
/// <param name="Slot">The slot number on the blockchain.</param>
/// <param name="Hash">The block header hash identifying the block at this slot.</param>
[CborSerializable]
[CborList]
public partial record SpecificPoint(
    [CborOrder(0)] ulong Slot,
    [CborOrder(1)] ReadOnlyMemory<byte> Hash
) : Point;

/// <summary>
/// A CBOR-serializable list of <see cref="Point"/> values, used in ChainSync FindIntersect requests.
/// </summary>
/// <param name="Value">The list of chain points.</param>
[CborSerializable]
public partial record Points(List<Point> Value) : CborRecord;
