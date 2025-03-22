
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

[CborSerializable]
[CborMap]
public partial record TestMap(
    [CborProperty(0)]
    int Value1,
    [CborProperty(1)]
    byte[] Value2,
    [CborProperty(2)]
    string Value3,
    [CborProperty(3)]
    bool Value4,
    [CborProperty(4)]
    long Value6
) : CborBase<TestMap>;