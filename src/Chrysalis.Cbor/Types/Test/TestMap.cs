
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

//// [CborSerializable]
[CborMap]
public partial record TestMap(
    [property: CborProperty("0")]
    int Value1,
    [property: CborProperty("1")]
    byte[] Value2,
    [property: CborProperty("2")]
    string Value3,
    [property: CborProperty("3")]
    bool Value4,
    [property: CborProperty("4")]
    long Value6
) : CborBase<TestMap>;