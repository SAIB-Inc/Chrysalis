using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborConstr(0)]
public readonly partial record struct PlutusVoid : ICborType;
