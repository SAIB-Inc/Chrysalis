
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

// [CborSerializable]
[CborList]
public partial record TestList(
    [CborOrder(0)]
    int Value1,
    [CborOrder(1)]
    List<TestConstr> Value2,
    Dictionary<string, TestConstr> Value3
) : CborBase<TestList>;