using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

// Creating a nullable serializable type
[CborSerializable]
[CborMap]
public partial record NullableTestMap(
    [CborProperty(0)]
    int Value1,
    [CborProperty(1)]
    string Value2
) : CborBase<NullableTestMap>;

[CborSerializable]
public partial record CborContainerSimpleTest(int Value1) : CborBase<NullableTestMap>;

[CborSerializable]
public partial record CborContainerConstrTest(TestConstr Value1) : CborBase<CborContainerConstrTest>;

[CborSerializable]
public partial record CborContainerGenericTest<T>(T Value1) : CborBase<CborContainerGenericTest<T>>;