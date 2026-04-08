using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborConstr]
[CborUnion]
public partial interface ICborOption<T> : ICborType;

[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public readonly partial record struct Some<T> : ICborOption<T>
{
    [CborOrder(0)] public partial T Value { get; }
}

[CborSerializable]
[CborConstr(1)]
public readonly partial record struct None<T> : ICborOption<T>;
