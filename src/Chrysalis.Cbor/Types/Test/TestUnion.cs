using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

[CborSerializable]
[CborUnion]
public abstract partial record TestUnion : CborBase<TestUnion>
{
    // [CborSerializable]
    [CborList]
    public partial record TestListUnion(
        [CborOrder(0)]
        int Value1,
        [CborOrder(1)]
        List<TestConstr> Value2,
        Dictionary<string, TestConstr> Value3
    ) : TestUnion;

    // [CborSerializable]
    [CborConstr(0)]
    public partial record TestConstrUnion(
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
    ) : TestUnion;

    // [CborSerializable]
    [CborMap]
    public partial record TestMapUnion(
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
    ) : TestUnion;


    // [CborSerializable]
    [CborMap]
    public partial record NullableTestMapUnion(
        [property: CborProperty("0")]
        int Value1,
        [property: CborProperty("1")]
        [CborNullable]
        string Value2
    ) : TestUnion;
}