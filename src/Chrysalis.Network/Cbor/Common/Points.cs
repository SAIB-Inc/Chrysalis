using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Common;

[CborSerializable]
[CborList]
public partial record Point(
    [CborOrder(0)] ulong Slot,
    [CborOrder(1)] byte[] Hash
) : CborBase;

[CborSerializable]
public partial record Points(List<Point> Value) : CborBase;