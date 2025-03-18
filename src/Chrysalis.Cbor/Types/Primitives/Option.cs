using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Primitives;

[CborSerializable]
[CborUnion]
public abstract partial record Option<T> : CborBase<Option<T>>
{
    [CborSerializable]
    [CborConstr(0)]
    public partial record Some<U>([CborOrder(0)] U Value) : Option<U>;


    [CborSerializable]
    [CborConstr(1)]
    public partial record None<U> : Option<U>;
}