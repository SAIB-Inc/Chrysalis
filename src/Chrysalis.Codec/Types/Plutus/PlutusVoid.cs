using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborConstr(0)]
public readonly partial record struct PlutusVoid : IPlutusData;
