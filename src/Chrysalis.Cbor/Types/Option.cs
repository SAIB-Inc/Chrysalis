using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

[CborSerializable]
[CborUnion]
public abstract partial record Option<T> : CborBase { }

[CborSerializable]
[CborConstr(0)]
public partial record Some<T>([CborOrder(0)] T Value) : Option<T>;


[CborSerializable]
[CborConstr(1)]
public partial record None<T> : Option<T>;
