using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types;

[CborSerializable]
[CborConstr]
[CborUnion]
public partial interface ICborOption<T> : ICborType;

[CborSerializable]
[CborConstr(0)]
public readonly partial record struct Some<T> : ICborOption<T>
{
    [CborOrder(0)] public partial T Value { get; }
}

[CborSerializable]
[CborConstr(1)]
public readonly partial record struct None<T> : ICborOption<T>;
