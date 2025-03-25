
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Cbor.Types.Test;

[CborSerializable]
[CborList]
public partial record TestList(
    [CborOrder(0)]
    int Value1,
    [CborOrder(1)]
    List<TestConstr> Value2,
    [CborOrder(2)]
    Dictionary<string, TestConstr> Value3
) : CborBase;

[CborSerializable]
[CborList]
public partial record TestListMaybe(
    [CborOrder(0)]
    CborMaybeIndefList<TestList> Value1
) : CborBase;