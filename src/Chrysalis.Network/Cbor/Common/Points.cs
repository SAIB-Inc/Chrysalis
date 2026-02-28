using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Common;

[CborSerializable]
[CborUnion]
public abstract partial record Point : CborBase
{
    public static Point Origin => new OriginPoint();
    public static Point Specific(ulong slot, ReadOnlyMemory<byte> hash)
    {
        return new SpecificPoint(slot, hash);
    }
}

[CborSerializable]
[CborList]
public partial record OriginPoint() : Point;

[CborSerializable]
[CborList]
public partial record SpecificPoint(
    [CborOrder(0)] ulong Slot,
    [CborOrder(1)] ReadOnlyMemory<byte> Hash
) : Point;

[CborSerializable]
public partial record Points(List<Point> Value) : CborBase;