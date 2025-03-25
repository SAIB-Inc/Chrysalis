using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Common;

[CborSerializable]
[CborList]
public partial record Tip(
    [CborOrder(0)] Point Slot,
    [CborOrder(1)] int BlockNumber
) : CborBase;
