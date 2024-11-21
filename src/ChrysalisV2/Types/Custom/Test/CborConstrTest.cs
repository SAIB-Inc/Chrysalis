using ChrysalisV2.Attributes;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Types.Custom.Test;

[CborSerializable(typeof(CborConstr), 1)]
public record CborConstrTestIndex1 : CborConstr;

[CborSerializable(typeof(CborConstr), 0)]
public record CborConstrTestWithParams(
    CborBytes ShortBytes,
    CborBoundedBytesTest LongBytes
) : CborConstr;