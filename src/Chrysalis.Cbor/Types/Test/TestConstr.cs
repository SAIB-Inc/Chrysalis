
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

[CborSerializable]
[CborConstr(0)]
public partial record TestConstr(
    [CborOrder(0)]
    int Value1,
    [CborOrder(1)]
    byte[] Value2,
    [CborOrder(2)]
    string Value3,
    [CborOrder(3)]
    bool Value4,
    [CborOrder(4)]
    long Value6,
    [CborOrder(5)]
    float Value7,
    [CborOrder(6)]
    double Value8,
    [CborOrder(7)]
    decimal Value9,
    [CborOrder(8)]
    uint Value11,
    [CborOrder(9)]
    ulong Value12
) : CborBase<TestConstr>;